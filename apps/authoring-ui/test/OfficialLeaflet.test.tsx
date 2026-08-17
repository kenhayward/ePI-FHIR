import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { OfficialLeaflet } from '../src/OfficialLeaflet';
import type { FiledRender, OfficialRenderOutcome } from '../src/platform/client';

// The artefact of record, on a screen (FN-AUT-016).
//   CAP-RND-002 Store rendered output as immutable assets
//   CAP-RND-004 Distinguish an author preview from an official render
//
// The preview panel next door shows what content reads like and says it is not the filed
// artefact. This one shows the filed artefact, and the distinction has to survive being on a
// screen: a person who cannot tell which of the two they are looking at is a person who will
// eventually send the wrong one.
//
// Producing one is a button rather than a page load, because it files something. A screen that
// filed an artefact as a side effect of being opened would be filing on somebody's behalf.
describe('FN-AUT-016 the artefact of record', () => {
  const html = '<!DOCTYPE html><html><body><h1>SYNTHETIC - Examplinum</h1></body></html>';

  const filed: FiledRender = {
    template: 'qrd-package-leaflet',
    templateVersion: 1,
    key: 'rendered/doc-1/2/qrd-package-leaflet/1/final.html',
    mediaType: 'text/html; charset=utf-8',
    alreadyFiled: false,
  };

  const templates = [
    { identifier: 'qrd-package-leaflet', version: 1, name: 'EU QRD package leaflet' },
    { identifier: 'qrd-labelling', version: 1, name: 'EU QRD labelling' },
  ];

  const panel = (over: {
    readonly templates?: () => Promise<readonly typeof templates[number][]>;
    readonly filedRenders?: () => Promise<readonly FiledRender[]>;
    readonly produce?: (template: string) => Promise<OfficialRenderOutcome>;
    readonly artefact?: (template: string, templateVersion: number) => Promise<string>;
  } = {}) => (
    <OfficialLeaflet
      approvedTemplates={over.templates ?? vi.fn(async () => templates)}
      filedRenders={over.filedRenders ?? vi.fn(async () => [] as readonly FiledRender[])}
      produce={over.produce ?? vi.fn(async () => ({ ok: true, render: filed }) as OfficialRenderOutcome)}
      artefact={over.artefact ?? vi.fn(async () => html)}
    />
  );

  it('offers the templates somebody has approved', async () => {
    render(panel());

    expect(await screen.findByRole('option', { name: /EU QRD package leaflet/i })).toBeDefined();
  });

  it('files nothing until somebody asks', async () => {
    // The whole reason this is a button. Opening a screen is not a decision to produce the
    // document a regulator is sent.
    const produce = vi.fn(async () => ({ ok: true, render: filed }) as OfficialRenderOutcome);

    render(panel({ produce }));
    await screen.findByRole('option', { name: /EU QRD package leaflet/i });

    expect(produce).not.toHaveBeenCalled();
  });

  it('produces the artefact with the template that was chosen', async () => {
    const produce = vi.fn(async () => ({ ok: true, render: filed }) as OfficialRenderOutcome);
    render(panel({ produce }));

    await screen.findByRole('option', { name: /EU QRD labelling/i });
    await userEvent.selectOptions(screen.getByLabelText(/template/i), 'qrd-labelling');
    await userEvent.click(screen.getByRole('button', { name: /produce/i }));

    await waitFor(() => expect(produce).toHaveBeenCalledWith('qrd-labelling'));
  });

  it('shows the artefact in a sandboxed frame, never in this page', async () => {
    // The same rule as the preview: it is the platform's own output and it is still a document
    // assembled from content people type. This page's origin, session and token are none of its
    // business.
    render(panel());

    await screen.findByRole('option', { name: /EU QRD package leaflet/i });
    await userEvent.click(screen.getByRole('button', { name: /produce/i }));

    const frame = await screen.findByTitle(/filed artefact/i);
    expect(frame.tagName).toBe('IFRAME');
    expect(frame.getAttribute('sandbox')).toBe('');
    expect(frame.getAttribute('srcdoc')).toContain('Examplinum');
  });

  it('says where the artefact is filed, so it can be cited', async () => {
    // The key is the citation. An artefact somebody can see and cannot refer to is one they
    // cannot point a regulator at.
    render(panel());

    await screen.findByRole('option', { name: /EU QRD package leaflet/i });
    await userEvent.click(screen.getByRole('button', { name: /produce/i }));

    expect(await screen.findByText(/rendered\/doc-1\/2\/qrd-package-leaflet\/1/)).toBeDefined();
  });

  it('never calls this a preview', async () => {
    render(panel());

    await screen.findByRole('option', { name: /EU QRD package leaflet/i });
    await userEvent.click(screen.getByRole('button', { name: /produce/i }));
    await screen.findByTitle(/filed artefact/i);

    expect(screen.queryByText(/preview/i)).toBeNull();
  });

  it('says when the artefact was already filed rather than made now', async () => {
    // A render is a pure function of its two versions, so a second request returns the first
    // one's bytes (ADR-046 decision 4). Saying "filed" for both would tell an author they had
    // just produced something they had not.
    const produce = vi.fn(async () =>
      ({ ok: true, render: { ...filed, alreadyFiled: true } }) as OfficialRenderOutcome);

    render(panel({ produce }));
    await screen.findByRole('option', { name: /EU QRD package leaflet/i });
    await userEvent.click(screen.getByRole('button', { name: /produce/i }));

    expect(await screen.findByText(/already filed/i)).toBeDefined();
  });

  it('shows what the platform refused, rather than that something went wrong', async () => {
    const produce = vi.fn(async () =>
      ({
        ok: false,
        kind: 'refused',
        detail: 'Version 2 is in-review, and only an approved version has an official render.',
      }) as OfficialRenderOutcome);

    render(panel({ produce }));
    await screen.findByRole('option', { name: /EU QRD package leaflet/i });
    await userEvent.click(screen.getByRole('button', { name: /produce/i }));

    expect(await screen.findByText(/only an approved version/i)).toBeDefined();
  });

  it('says a deployment with no approved template has nothing to render with', async () => {
    // The state a fresh deployment is in, and the remedy is somebody approving a template rather
    // than anything an author can do here (ADR-042 decision 7).
    render(panel({ templates: vi.fn(async () => []) }));

    expect(await screen.findByText(/no approved template/i)).toBeDefined();
    expect(screen.queryByRole('button', { name: /produce/i })).toBeNull();
  });

  it('offers what has already been filed without producing anything', async () => {
    // So a screen shows the artefact that exists rather than asking for it to be made again.
    const produce = vi.fn();

    render(panel({
      filedRenders: vi.fn(async () => [{ ...filed, alreadyFiled: true }]),
      produce: produce as unknown as (t: string) => Promise<OfficialRenderOutcome>,
    }));

    expect(await screen.findByTitle(/filed artefact/i)).toBeDefined();
    expect(produce).not.toHaveBeenCalled();
  });

  it('says a template list that could not be read was not read', async () => {
    render(panel({
      templates: vi.fn(async () => {
        throw new Error('The platform answered 503.');
      }),
    }));

    expect(await screen.findByRole('alert')).toBeDefined();
  });
});
