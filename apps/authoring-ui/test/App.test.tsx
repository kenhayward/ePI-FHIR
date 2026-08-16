import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import type { SaveOutcome } from '../src/platform/client';
import { paragraph, serialiseNarrative, text } from '../src/authoring/narrative';

// The parts, joined into something an author can use (FN-AUT-006).
//   CAP-TPL-005 Template-driven guided authoring that shields authors from raw FHIR
//   CAP-IAM-001 Authenticate through the enterprise identity provider
//
// The cases that matter here are the unhappy ones. #70 built a client that carries the
// platform's refusals faithfully, and a refusal that reaches no screen is a refusal that did not
// happen as far as the author is concerned.
describe('FN-AUT-006 the authoring application', () => {
  const narrative = (words: string) => serialiseNarrative([paragraph(text(words))]);

  const version = {
    documentIdentifier: '01a00000-0000-7000-8000-00000000000a',
    version: 2,
    state: 'draft',
    editable: true,
    sections: [
      { identity: 'sec-1', title: '1. What Examplinum is', narrative: narrative('A medicine.') },
    ],
  };

  const platform = (outcome: SaveOutcome = { ok: true, version: 3 }) => ({
    loadVersion: vi.fn(async () => version),
    saveSections: vi.fn(async () => outcome),
    searchLabels: vi.fn(async () => ({
      total: 1,
      page: 1,
      pageSize: 20,
      hits: [
        {
          documentIdentifier: '01a00000-0000-7000-8000-00000000000a',
          version: 2,
          title: 'SYNTHETIC - Examplinum 10 mg tablets',
          market: 'GB',
          state: 'draft',
        },
      ],
    })),
  });

  const session = (signedIn: boolean) => ({
    hasValidToken: signedIn,
    beginAsync: vi.fn(async () => 'https://keycloak.example.org/authorize'),
    completeAsync: vi.fn(async () => {}),
  });

  const at = (query: string) => new URL(`https://epi.example.org/${query}`);

  const label = '?label=01a00000-0000-7000-8000-00000000000a&version=2';

  it('asks a signed-out author to sign in, and shows them no label', async () => {
    render(
      <App session={session(false)} platform={platform()} location={at(label)} go={vi.fn()} />,
    );

    expect(screen.getByRole('button', { name: /sign in/i })).toBeDefined();
    expect(screen.queryByRole('heading', { name: '1. What Examplinum is' })).toBeNull();
  });

  it('sends the author to the identity provider rather than asking for a password', async () => {
    const go = vi.fn();
    const signIn = session(false);
    render(<App session={signIn} platform={platform()} location={at(label)} go={go} />);

    await userEvent.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(go).toHaveBeenCalledWith('https://keycloak.example.org/authorize'));
    expect(screen.queryByLabelText(/password/i)).toBeNull();
  });

  it('completes the sign-in when the identity provider sends the author back', async () => {
    const signIn = session(false);
    render(
      <App
        session={signIn}
        platform={platform()}
        location={at('callback?code=abc&state=xyz')}
        go={vi.fn()}
      />,
    );

    await waitFor(() => expect(signIn.completeAsync).toHaveBeenCalled());
  });

  it('opens the label the address names, once there is a token', async () => {
    const client = platform();
    render(<App session={session(true)} platform={client} location={at(label)} go={vi.fn()} />);

    await waitFor(() =>
      expect(client.loadVersion).toHaveBeenCalledWith('01a00000-0000-7000-8000-00000000000a', 2),
    );
    expect(await screen.findByRole('heading', { name: '1. What Examplinum is' })).toBeDefined();
  });

  it('offers a way to find a label when the address names none', async () => {
    // It used to say the address did not name one and stop there, which is a dead end: an
    // author with no link had nowhere to go.
    render(<App session={session(true)} platform={platform()} location={at('')} go={vi.fn()} />);

    expect(await screen.findByRole('heading', { name: /find a label/i })).toBeDefined();
  });

  it('opens a label the author picked out of a search', async () => {
    const client = platform();
    render(<App session={session(true)} platform={client} location={at('')} go={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: /^search$/i }));
    await userEvent.click(await screen.findByRole('button', { name: /Examplinum/ }));

    await waitFor(() =>
      expect(client.loadVersion).toHaveBeenCalledWith('01a00000-0000-7000-8000-00000000000a', 2),
    );
  });

  it('does not search on behalf of somebody who has not signed in', async () => {
    // The search is permission-scoped, so an unauthenticated one is not a smaller search - it
    // is a request that will be refused, and offering it invites the author to make it.
    const client = platform();
    render(<App session={session(false)} platform={client} location={at('')} go={vi.fn()} />);

    expect(screen.queryByRole('heading', { name: /find a label/i })).toBeNull();
    expect(client.searchLabels).not.toHaveBeenCalled();
  });

  it('tells the author which version a save created', async () => {
    const client = platform({ ok: true, version: 3 });
    render(<App session={session(true)} platform={client} location={at(label)} go={vi.fn()} />);

    const box = await screen.findByLabelText('1. What Examplinum is');
    await userEvent.clear(box);
    await userEvent.type(box, 'A medicine for adults.');
    await userEvent.click(screen.getByRole('button', { name: /^save as version/i }));

    // Specific, because the save button also says "version 3" - the message has to be the one
    // that reports what happened, not the one that offered it.
    expect(await screen.findByText(/^Saved as version 3\./)).toBeDefined();
  });

  it('shows the platform its own words when it refuses a save', async () => {
    // The whole point of the client carrying problems rather than summarising them. An author
    // reading a paraphrase of a validation failure cannot find the thing it refers to.
    const client = platform({
      ok: false,
      kind: 'refused',
      problems: ['Composition.section[0].text: narrative must be an XHTML div'],
    });
    render(<App session={session(true)} platform={client} location={at(label)} go={vi.fn()} />);

    const box = await screen.findByLabelText('1. What Examplinum is');
    await userEvent.clear(box);
    await userEvent.type(box, 'Something.');
    await userEvent.click(screen.getByRole('button', { name: /^save as version/i }));

    expect(await screen.findByText(/narrative must be an XHTML div/)).toBeDefined();
  });

  it('says nothing was lost when the platform could not be reached', async () => {
    // Distinct from a refusal: nothing was decided, so retrying is safe. An author who thinks
    // their work went somewhere will close the tab.
    const client = platform({ ok: false, kind: 'unreachable', detail: 'Failed to fetch' });
    render(<App session={session(true)} platform={client} location={at(label)} go={vi.fn()} />);

    const box = await screen.findByLabelText('1. What Examplinum is');
    await userEvent.clear(box);
    await userEvent.type(box, 'Something.');
    await userEvent.click(screen.getByRole('button', { name: /^save as version/i }));

    expect(await screen.findByText(/nothing was saved and nothing was lost/i)).toBeDefined();
  });

  it('asks a signed-out author to sign in again rather than reporting a failure', async () => {
    const client = platform({ ok: false, kind: 'unauthenticated' });
    render(<App session={session(true)} platform={client} location={at(label)} go={vi.fn()} />);

    const box = await screen.findByLabelText('1. What Examplinum is');
    await userEvent.clear(box);
    await userEvent.type(box, 'Something.');
    await userEvent.click(screen.getByRole('button', { name: /^save as version/i }));

    expect(await screen.findByText(/sign in again/i)).toBeDefined();
  });

  it('says who to ask when the platform forbids the save', async () => {
    const client = platform({ ok: false, kind: 'forbidden', detail: 'Access denied for author' });
    render(<App session={session(true)} platform={client} location={at(label)} go={vi.fn()} />);

    const box = await screen.findByLabelText('1. What Examplinum is');
    await userEvent.clear(box);
    await userEvent.type(box, 'Something.');
    await userEvent.click(screen.getByRole('button', { name: /^save as version/i }));

    expect(await screen.findByText(/not allowed/i)).toBeDefined();
  });

  it('tells the author to reload when somebody else got there first', async () => {
    // Retrying would overwrite whatever they wrote, so the remedy is to look before deciding.
    const client = platform({ ok: false, kind: 'conflict', detail: 'version 3 already exists' });
    render(<App session={session(true)} platform={client} location={at(label)} go={vi.fn()} />);

    const box = await screen.findByLabelText('1. What Examplinum is');
    await userEvent.clear(box);
    await userEvent.type(box, 'Something.');
    await userEvent.click(screen.getByRole('button', { name: /^save as version/i }));

    expect(await screen.findByText(/somebody else/i)).toBeDefined();
  });

  it('says so when a label cannot be opened, rather than showing nothing', async () => {
    const client = {
      loadVersion: vi.fn(async () => {
        throw new Error('Version 2 of that label was not found.');
      }),
      saveSections: vi.fn(async (): Promise<SaveOutcome> => ({ ok: true, version: 3 })),
      searchLabels: vi.fn(async () => ({ total: 0, page: 1, pageSize: 20, hits: [] })),
    };
    render(<App session={session(true)} platform={client} location={at(label)} go={vi.fn()} />);

    expect(await screen.findByText(/was not found/i)).toBeDefined();
  });
});
