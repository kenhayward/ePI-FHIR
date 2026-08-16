import { describe, expect, it, vi } from 'vitest';
import { PlatformClient } from '../src/platform/client';

// The surface's only way to reach the platform (FN-AUT-004).
//   CAP-TPL-005 Template-driven guided authoring that shields authors from raw FHIR
//
// ADR-038's endpoints, from the browser. Two things this has to get right and nothing else
// does: it must never invent a section the platform did not send, and it must tell an author
// what the platform refused rather than reporting "something went wrong" - a save rejected at
// the write gate carries the reasons, and losing them means the author cannot act on them.
describe('FN-AUT-004 the platform client', () => {
  const version = {
    documentIdentifier: '01a00000-0000-7000-8000-00000000000a',
    version: 2,
    state: 'draft',
    editable: true,
    sections: [
      { identity: 'sec-1', title: '1. What it is', narrative: '<div/>' },
      { identity: 'sec-2', title: '2. Before', narrative: '<div/>' },
    ],
  };

  // Typed with fetch's own signature, so the assertions about what was sent are checked rather
  // than cast into existence.
  const respondWith = (status: number, body: unknown) =>
    vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) =>
      new Response(JSON.stringify(body), {
        status,
        headers: { 'content-type': 'application/json' },
      }),
    );

  const clientOver = (fetcher: typeof fetch, token = 'a-token') =>
    new PlatformClient({ baseUrl: 'https://epi.example.org', token: async () => token, fetcher });

  it('loads a version as the sections the platform described', async () => {
    const client = clientOver(respondWith(200, version) as unknown as typeof fetch);

    const loaded = await client.loadVersion('01a00000-0000-7000-8000-00000000000a', 2);

    expect(loaded.sections.map((s) => s.identity)).toEqual(['sec-1', 'sec-2']);
    expect(loaded.editable).toBe(true);
    expect(loaded.state).toBe('draft');
  });

  it('sends the access token, and never the token in a query string', async () => {
    // A token in a URL is a token in every access log and every referrer header.
    const fetcher = respondWith(200, version);
    await clientOver(fetcher as unknown as typeof fetch).loadVersion('doc-1', 2);

    const [url, init] = fetcher.mock.calls[0]!;
    expect(String(url)).not.toContain('a-token');
    expect(new Headers(init?.headers).get('authorization')).toBe('Bearer a-token');
  });

  it('saves sections and reports the version that was minted', async () => {
    // Saving never changes the version that was read; it mints the next one
    // (ADR-038 decision 6).
    const fetcher = respondWith(201, { documentIdentifier: 'doc-1', version: 3 });

    const saved = await clientOver(fetcher as unknown as typeof fetch).saveSections('doc-1', 2, [
      { identity: 'sec-1', title: '1. What it is', narrative: '<div/>' },
    ]);

    expect(saved).toEqual({ ok: true, version: 3 });
    const [, init] = fetcher.mock.calls[0]!;
    expect(init?.method).toBe('POST');
  });

  it('hands back what the write gate refused, in the words it refused it', async () => {
    // The one that matters most. A save rejected by validation carries the reasons; reporting
    // "something went wrong" leaves an author with content they cannot fix and no idea why.
    const fetcher = respondWith(400, {
      problems: ['Composition.section[0].text: narrative must be an XHTML div'],
    });

    const saved = await clientOver(fetcher as unknown as typeof fetch).saveSections('doc-1', 2, []);

    expect(saved).toEqual({
      ok: false,
      kind: 'refused',
      problems: ['Composition.section[0].text: narrative must be an XHTML div'],
    });
  });

  it('tells a signed-out author from a forbidden one', async () => {
    // Different problems with different remedies: sign in again, or ask for access. Collapsing
    // them into one message makes both unactionable.
    const unauthorised = await clientOver(
      respondWith(401, {}) as unknown as typeof fetch,
    ).saveSections('doc-1', 2, []);
    const forbidden = await clientOver(
      respondWith(403, { detail: 'Access denied for action author' }) as unknown as typeof fetch,
    ).saveSections('doc-1', 2, []);

    expect(unauthorised).toMatchObject({ ok: false, kind: 'unauthenticated' });
    expect(forbidden).toMatchObject({ ok: false, kind: 'forbidden' });
  });

  it('reports a conflict as a conflict, because the remedy is to reload', async () => {
    // Somebody else minted the next version first. Retrying the same save would overwrite
    // whatever they wrote, so the author has to see the current version before deciding.
    const saved = await clientOver(
      respondWith(409, { detail: 'version 3 already exists' }) as unknown as typeof fetch,
    ).saveSections('doc-1', 2, []);

    expect(saved).toMatchObject({ ok: false, kind: 'conflict' });
  });

  it('reports a version nobody wrote as not found rather than as an error', async () => {
    const client = clientOver(respondWith(404, {}) as unknown as typeof fetch);

    await expect(client.loadVersion('doc-nope', 1)).rejects.toThrow(/not found/i);
  });

  it('refuses to save a section the platform did not send', async () => {
    // Guarding the invariant on this side too: the platform refuses it, and a client that sent
    // one would be asking for a section to be invented (ADR-038 decision 4).
    const fetcher = respondWith(200, version);
    const client = clientOver(fetcher as unknown as typeof fetch);
    await client.loadVersion('doc-1', 2);

    await expect(
      client.saveSections('doc-1', 2, [
        { identity: 'sec-invented', title: 'Invented', narrative: '<div/>' },
      ]),
    ).rejects.toThrow(/sec-invented/);
  });

  it('says plainly when the platform could not be reached at all', async () => {
    // Distinct from a refusal: nothing was decided, so nothing was lost, and retrying is safe.
    const client = clientOver(
      vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit): Promise<Response> => {
        throw new TypeError('Failed to fetch');
      }) as unknown as typeof fetch,
    );

    expect(await client.saveSections('doc-1', 2, [])).toMatchObject({
      ok: false,
      kind: 'unreachable',
    });
  });
});
