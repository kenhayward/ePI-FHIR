import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LeafletPreview } from '../src/LeafletPreview';

// Seeing the leaflet the content produces (FN-AUT-015).
//   CAP-RND-001 Render FHIR ePI to accessible HTML
//   CAP-RND-004 A draft render is distinguishable from an official one
//
// The artefact a regulatory affairs professional recognises, and the first time the platform's
// rendering has reached a screen. Two things matter here beyond showing it: the HTML is put in
// a sandboxed frame rather than into this page, and it is never presented as anything but a
// preview.
describe('FN-AUT-015 seeing the leaflet', () => {
  const html = '<!DOCTYPE html><html><body><h1>SYNTHETIC - Examplinum</h1></body></html>';

  it('shows the rendered leaflet', async () => {
    render(<LeafletPreview load={vi.fn(async () => html)} />);

    const frame = await screen.findByTitle(/preview of this version/i);
    expect(frame).toBeDefined();
    expect(frame.getAttribute('srcdoc')).toContain('Examplinum');
  });

  it('puts the rendered HTML in a sandboxed frame, never into this page', async () => {
    // It is the platform's own output and it is still a document assembled from content people
    // type. Rendering it into this page would give it this page's origin, its session and its
    // token; a sandboxed frame gives it none of them.
    render(<LeafletPreview load={vi.fn(async () => html)} />);

    const frame = await screen.findByTitle(/preview of this version/i);
    expect(frame.tagName).toBe('IFRAME');
    expect(frame.getAttribute('sandbox')).toBe('');
  });

  it('says it is a preview rather than the filed artefact', async () => {
    // CAP-RND-004, and the honest limit: no template store exists, so nothing here is rendered
    // with a template anybody approved (ADR-033 decision 2).
    render(<LeafletPreview load={vi.fn(async () => html)} />);

    expect(await screen.findByText(/not the artefact/i)).toBeDefined();
  });

  it('says a preview that could not be made was not made', async () => {
    const failing = vi.fn(async () => {
      throw new Error('The platform answered 503.');
    });
    render(<LeafletPreview load={failing} />);

    expect(await screen.findByRole('alert')).toBeDefined();
    expect(screen.queryByTitle(/preview of this version/i)).toBeNull();
  });

  it('asks for the preview once, not on every render', async () => {
    // A preview is a render on the server; asking for it repeatedly turns looking at a leaflet
    // into load.
    const load = vi.fn(async () => html);
    const { rerender } = render(<LeafletPreview load={load} />);

    rerender(<LeafletPreview load={load} />);
    await waitFor(() => expect(load).toHaveBeenCalledTimes(1));
  });
});
