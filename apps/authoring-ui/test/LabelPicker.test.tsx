import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { LabelPicker } from '../src/LabelPicker';
import type { SearchResults } from '../src/platform/client';

// Finding a label to open (FN-AUT-007).
//   CAP-SCH-004 Search is scoped to what the caller is permitted to see
//   CAP-TPL-005 Template-driven guided authoring that shields authors from raw FHIR
//
// The platform bounds a search by what the caller may see rather than filtering its results
// (ADR-022 decision 1). That makes an empty answer genuinely ambiguous - there may be no such
// label, or there may be one this person is not allowed to know about - and the platform
// deliberately will not say which. A screen that reported "no labels found" would be resolving
// that ambiguity in the one direction the design refuses to.
describe('FN-AUT-007 finding a label', () => {
  const results = (hits: SearchResults['hits'], total = hits.length): SearchResults => ({
    total,
    page: 1,
    pageSize: 20,
    hits,
  });

  const hit = {
    documentIdentifier: '01a00000-0000-7000-8000-00000000000a',
    version: 2,
    title: 'SYNTHETIC - Examplinum 10 mg tablets',
    market: 'GB',
    state: 'approved',
  };

  it('searches for what the author typed', async () => {
    const search = vi.fn(async () => results([hit]));
    render(<LabelPicker search={search} onOpen={vi.fn()} />);

    await userEvent.type(screen.getByLabelText(/search/i), 'examplinum');
    await userEvent.click(screen.getByRole('button', { name: /search/i }));

    expect(search).toHaveBeenCalledWith({ text: 'examplinum' });
  });

  it('shows each label with the version, market and state it is in', async () => {
    // A title alone is not enough to choose by: the same label exists in several markets and
    // several versions, and opening the wrong one is a wasted edit at best.
    render(<LabelPicker search={vi.fn(async () => results([hit]))} onOpen={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: /search/i }));

    expect(await screen.findByText(/SYNTHETIC - Examplinum 10 mg tablets/)).toBeDefined();
    expect(screen.getByText(/version 2/i)).toBeDefined();
    expect(screen.getByText(/GB/)).toBeDefined();
    expect(screen.getByText(/approved/i)).toBeDefined();
  });

  it('opens the label and version the author picked', async () => {
    const onOpen = vi.fn();
    render(<LabelPicker search={vi.fn(async () => results([hit]))} onOpen={onOpen} />);

    await userEvent.click(screen.getByRole('button', { name: /search/i }));
    await userEvent.click(await screen.findByRole('button', { name: /Examplinum/ }));

    expect(onOpen).toHaveBeenCalledWith('01a00000-0000-7000-8000-00000000000a', 2);
  });

  it('says an empty answer may mean nothing this author may see', async () => {
    // Not "no labels found". The platform will not distinguish a label that does not exist from
    // one outside this caller's scope, and neither should the screen.
    render(<LabelPicker search={vi.fn(async () => results([]))} onOpen={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: /search/i }));

    expect(await screen.findByText(/allowed to see/i)).toBeDefined();
  });

  it('says how many it is showing of how many there are', async () => {
    // Somebody shown twenty of thirty-four who assumes that is all of them will conclude a
    // label does not exist.
    render(<LabelPicker search={vi.fn(async () => results([hit], 34))} onOpen={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: /search/i }));

    expect(await screen.findByText(/of 34/)).toBeDefined();
  });

  it('says a search failed rather than showing it as empty', async () => {
    // A failure presented as "no results" is a lie, and it sends the author looking for a label
    // that is there.
    const search = vi.fn(async () => {
      throw new Error('The platform answered 503 to that search.');
    });
    render(<LabelPicker search={search} onOpen={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: /search/i }));

    expect(await screen.findByRole('alert')).toBeDefined();
    expect(screen.queryByText(/allowed to see/i)).toBeNull();
  });

  it('shows nothing at all before anybody has searched', async () => {
    // An empty result and a search nobody has run yet are different things, and only one of
    // them says anything about what exists.
    render(<LabelPicker search={vi.fn(async () => results([]))} onOpen={vi.fn()} />);

    expect(screen.queryByText(/allowed to see/i)).toBeNull();
    expect(screen.queryByRole('list')).toBeNull();
  });
});
