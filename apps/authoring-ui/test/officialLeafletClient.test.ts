import { describe, expect, it, vi } from 'vitest';
import { PlatformClient } from '../src/platform/client';

// Reaching the artefact of record from the browser (FN-AUT-016).
//   CAP-RND-002 Store rendered output as immutable assets
//   CAP-RND-004 Distinguish an author preview from an official render
//
// ADR-046 gave the platform an official render. This is how a surface asks for one, and the
// three things it has to get right: only approved templates may be offered, producing one is a
// request that either files something or says why not, and what comes back is a document rather
// than JSON.
describe('FN-AUT-016 reaching the artefact of record', () => {
  const respondWith = (status: number, body: unknown) =>
    vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) =>
      new Response(JSON.stringify(body), {
        status,
        headers: { 'content-type': 'application/json' },
      }),
    );

  const respondWithText = (status: number, text: string) =>
    vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) =>
      new Response(text, { status, headers: { 'content-type': 'text/html' } }),
    );

  const clientOver = (fetcher: typeof fetch) =>
    new PlatformClient({
      baseUrl: 'https://epi.example.org',
      token: async () => 'a-token',
      fetcher,
    });

  const templates = [
    { identifier: 'qrd-package-leaflet', version: 1, name: 'EU QRD leaflet', state: 'approved' },
    { identifier: 'qrd-labelling', version: 2, name: 'EU QRD labelling', state: 'draft' },
    { identifier: 'qrd-smpc', version: 1, name: 'EU QRD SmPC', state: 'retired' },
  ];

  it('offers only the templates somebody has approved', async () => {
    // A draft template cannot produce an official render (ADR-042 decision 4), so offering one
    // would be offering a choice the platform will refuse - and an author would reasonably
    // conclude the platform was broken rather than that they had picked an unapproved template.
    const client = clientOver(respondWith(200, templates) as unknown as typeof fetch);

    const usable = await client.approvedTemplatesAsync();

    expect(usable.map((t) => t.identifier)).toEqual(['qrd-package-leaflet']);
  });

  it('says a template list that could not be read was not read', async () => {
    const client = clientOver(respondWith(503, {}) as unknown as typeof fetch);

    await expect(client.approvedTemplatesAsync()).rejects.toThrow(/503/);
  });

  it('produces a render and reports what was filed', async () => {
    const fetcher = respondWith(201, {
      template: 'qrd-package-leaflet',
      templateVersion: 1,
      key: 'rendered/doc-1/2/qrd-package-leaflet/1/final.html',
      mediaType: 'text/html; charset=utf-8',
      alreadyFiled: false,
    });

    const filed = await clientOver(fetcher as unknown as typeof fetch)
      .produceRenderAsync('doc-1', 2, 'qrd-package-leaflet');

    expect(filed.ok).toBe(true);
    if (filed.ok) {
      expect(filed.render.key).toBe('rendered/doc-1/2/qrd-package-leaflet/1/final.html');
      expect(filed.render.alreadyFiled).toBe(false);
    }

    const [, init] = fetcher.mock.calls[0]!;
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({ template: 'qrd-package-leaflet' });
  });

  it('reports a second request as already filed rather than as a new artefact', async () => {
    // A render is a pure function of its two versions, so asking again asks for the same bytes
    // (ADR-046 decision 4). An author has to be able to tell "made now" from "made before".
    const fetcher = respondWith(200, {
      template: 'qrd-package-leaflet',
      templateVersion: 1,
      key: 'rendered/doc-1/2/qrd-package-leaflet/1/final.html',
      mediaType: 'text/html; charset=utf-8',
      alreadyFiled: true,
    });

    const filed = await clientOver(fetcher as unknown as typeof fetch)
      .produceRenderAsync('doc-1', 2, 'qrd-package-leaflet');

    expect(filed.ok && filed.render.alreadyFiled).toBe(true);
  });

  it('reports a refusal as a refusal, with what the platform said', async () => {
    // 409 means a rule would not have it - the version is not approved, or the template is not.
    // An author can act on that; "something went wrong" is not actionable.
    const fetcher = respondWith(409, {
      detail: 'Version 2 is in-review, and only an approved version has an official render.',
    });

    const filed = await clientOver(fetcher as unknown as typeof fetch)
      .produceRenderAsync('doc-1', 2, 'qrd-package-leaflet');

    expect(filed.ok).toBe(false);
    if (!filed.ok && filed.kind !== 'missing') {
      expect(filed.kind).toBe('refused');
      expect(filed.detail).toMatch(/only an approved version/i);
    }
  });

  it('distinguishes a version that is not there from one that was refused', async () => {
    const fetcher = respondWith(404, {});

    const filed = await clientOver(fetcher as unknown as typeof fetch)
      .produceRenderAsync('doc-1', 2, 'qrd-package-leaflet');

    expect(filed.ok).toBe(false);
    if (!filed.ok) {
      expect(filed.kind).toBe('missing');
    }
  });

  it('lists what has been filed for a version', async () => {
    const fetcher = respondWith(200, [
      {
        template: 'qrd-package-leaflet',
        templateVersion: 1,
        key: 'rendered/doc-1/2/qrd-package-leaflet/1/final.html',
        mediaType: 'text/html',
        alreadyFiled: true,
      },
    ]);

    const filed = await clientOver(fetcher as unknown as typeof fetch)
      .filedRendersAsync('doc-1', 2);

    expect(filed).toHaveLength(1);
    expect(filed[0]!.template).toBe('qrd-package-leaflet');
  });

  it('reads a filed artefact back as a document rather than as JSON', async () => {
    // What a regulator was sent is what was filed, so this reads the artefact rather than
    // asking for it to be rendered again (ADR-046 decision 6).
    const html = '<!DOCTYPE html><html><body><h1>SYNTHETIC - Examplinum</h1></body></html>';
    const fetcher = respondWithText(200, html);

    const artefact = await clientOver(fetcher as unknown as typeof fetch)
      .filedRenderAsync('doc-1', 2, 'qrd-package-leaflet', 1);

    expect(artefact).toContain('Examplinum');

    const [url] = fetcher.mock.calls[0]!;
    expect(String(url)).toContain('/renders/qrd-package-leaflet/1');
  });

  it('says a filed artefact that could not be read was not read', async () => {
    const client = clientOver(respondWithText(500, 'no') as unknown as typeof fetch);

    await expect(client.filedRenderAsync('doc-1', 2, 'qrd-package-leaflet', 1))
      .rejects.toThrow(/500/);
  });
});
