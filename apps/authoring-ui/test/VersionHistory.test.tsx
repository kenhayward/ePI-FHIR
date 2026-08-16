import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { VersionHistory } from '../src/VersionHistory';

// What happened to a version, who did it, and what they signed (FN-AUT-012).
//   CAP-LCM-006 Reconstruct the full content and metadata of any historical version
//   CAP-AUD-004 Electronic signatures are attributable and inspectable
//   CAP-LCM-011 Pin the content snapshot and its validating context at approval
//
// All of this has been recorded since iteration 2 and none of it has ever been shown. An audit
// trail nobody can read is an audit trail that exists for an inspection and not for the people
// doing the work - and the one thing on it that must never be subtle is a pinned package whose
// bytes no longer match, which is what tamper-evidence looks like when it fires.
describe('FN-AUT-012 what happened to a version', () => {
  const record = {
    state: 'approved',
    author: 'user-anna',
    contentHash: 'sha-256:abc123',
    packagesStillMatch: true,
    pinnedContext: {
      packages: [{ name: 'hl7.fhir.uv.emedicinal-product-info', version: '1.0.0' }],
      terminologyBindings: [
        { system: 'hl7.terminology.r5', version: '5.0.0', isVersioned: true },
      ],
    },
    history: [
      { from: 'draft', to: 'in-review', action: 'submit', actor: 'user-anna', at: '2026-08-16T09:00:00Z', signature: null },
      {
        from: 'in-review',
        to: 'approved',
        action: 'approve',
        actor: 'user-ben',
        at: '2026-08-16T10:00:00Z',
        signature: {
          printedName: 'Ben Okafor',
          meaning: 'approval',
          contentHash: 'sha-256:abc123',
          signedAt: '2026-08-16T10:00:00Z',
        },
      },
    ],
  };

  it('lists what happened, in order, with who did it and when', async () => {
    render(<VersionHistory load={vi.fn(async () => record)} />);

    expect(await screen.findByText(/submit/)).toBeDefined();
    expect(screen.getByText(/approve\b/)).toBeDefined();
    expect(screen.getByText(/user-ben/)).toBeDefined();
  });

  it('says what a signature asserted, and over what', async () => {
    // "Signed" is not the interesting part. What it meant, who it names, and which bytes it was
    // over are what make it evidence rather than a tick (ADR-020).
    render(<VersionHistory load={vi.fn(async () => record)} />);

    expect(await screen.findByText(/Ben Okafor/)).toBeDefined();
    expect(screen.getByText(/approval/)).toBeDefined();
    expect(screen.getByText(/sha-256:abc123/)).toBeDefined();
  });

  it('says which transitions carried no signature, rather than leaving a blank', async () => {
    // An unsigned transition is a fact about it, not a gap in the record. Submitting is not a
    // signed gate and should not read like one that lost its signature.
    render(<VersionHistory load={vi.fn(async () => record)} />);

    expect(await screen.findByText(/not a signed gate/i)).toBeDefined();
  });

  it('names what the version was validated against, terminology included', async () => {
    // ADR-036 put terminology in the pinned context so this question could be answered. A
    // screen showing packages and not terminology answers it incompletely.
    render(<VersionHistory load={vi.fn(async () => record)} />);

    expect(await screen.findByText(/emedicinal-product-info/)).toBeDefined();
    expect(screen.getByText(/hl7.terminology.r5/)).toBeDefined();
  });

  it('is loud when the pinned packages no longer match', async () => {
    // The one thing here that must never be subtle. It means what this version was validated
    // against has changed underneath it, which is the failure the whole pin exists to detect.
    render(
      <VersionHistory
        load={vi.fn(async () => ({ ...record, packagesStillMatch: false }))}
      />,
    );

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toMatch(/no longer match/i);
  });

  it('says nothing about matching when there was nothing pinned', async () => {
    // A version nobody approved has no pinned context, and "packages still match" would be an
    // assurance about something that does not exist.
    render(
      <VersionHistory
        load={vi.fn(async () => ({
          ...record,
          state: 'draft',
          pinnedContext: null,
          packagesStillMatch: true,
        }))}
      />,
    );

    expect(await screen.findByText(/not been approved/i)).toBeDefined();
    expect(screen.queryByText(/still match/i)).toBeNull();
  });

  it('says a history that could not be read was not read', async () => {
    const failing = vi.fn(async () => {
      throw new Error('The platform answered 503.');
    });
    render(<VersionHistory load={failing} />);

    expect(await screen.findByRole('alert')).toBeDefined();
  });
});
