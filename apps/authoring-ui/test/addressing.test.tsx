import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../src/App';

// The address is where you are (FN-AUT-019).
//   CAP-SCH-001 Find a label and open the version somebody meant
//   CAP-IAM-001 Authenticate via the enterprise identity provider
//
// Two things a browser makes obvious and no test had noticed.
//
// Opening a label from the picker left the address at the root, so the label an author was working
// on could not be bookmarked, shared or reloaded - a refresh took them back to the search box.
//
// Arriving at a label's address while signed out took them to the identity provider and back to
// the root, because the redirect URI is the origin and the query is dropped. Somebody following a
// link from an email landed on a search box with no idea which label they had been sent to.
describe('FN-AUT-019 the address is where you are', () => {
  const version = {
    documentIdentifier: '01a00000-0000-7000-8000-00000000000a',
    version: 2,
    state: 'draft',
    editable: true,
    sections: [{ identity: 'sec-1', title: '1. What Examplinum is', narrative: '<div/>' }],
  };

  const platform = () => ({
    loadVersion: vi.fn(async () => version),
    saveSections: vi.fn(async () => ({ ok: true as const, version: 3 })),
    transitionAsync: vi.fn(async () => ({ ok: true as const, from: 'draft', to: 'in-review' })),
    marketStandingsAsync: vi.fn(async () => ({ marketActions: {} })),
    marketTransitionAsync: vi.fn(async () => ({ ok: true as const, from: 'x', to: 'y' })),
    previewAsync: vi.fn(async () => '<!DOCTYPE html><html><body><p>Preview</p></body></html>'),
    approvedTemplatesAsync: vi.fn(async () => []),
    filedRendersAsync: vi.fn(async () => []),
    produceRenderAsync: vi.fn(async () => ({
      ok: false as const, kind: 'failed' as const, detail: 'no',
    })),
    filedRenderAsync: vi.fn(async () => '<!DOCTYPE html><html></html>'),
    versionRecordAsync: vi.fn(async () => ({
      state: 'draft',
      author: 'user-anna',
      contentHash: 'sha-256:abc',
      packagesStillMatch: true,
      pinnedContext: null,
      history: [],
    })),
    searchProducts: vi.fn(async () => []),
    openTasks: vi.fn(async () => []),
    signAsync: vi.fn(async () => ({ refused: false as const, reference: 'sig-1' })),
    searchLabels: vi.fn(async () => ({
      total: 1,
      page: 1,
      pageSize: 20,
      hits: [
        {
          documentIdentifier: version.documentIdentifier,
          version: 2,
          title: 'SYNTHETIC TEST LABEL - Examplinum',
          market: 'GB',
          state: 'draft',
        },
      ],
    })),
  });

  const signedIn = {
    hasValidToken: true,
    beginAsync: vi.fn(async () => 'https://keycloak.example.org/authorize'),
    completeAsync: vi.fn(async () => {}),
  };

  /** A sign-in that becomes valid when the exchange resolves, as the real one does. */
  const returning = () => {
    const session = {
      hasValidToken: false,
      beginAsync: vi.fn(async () => 'https://keycloak.example.org/authorize'),
      completeAsync: vi.fn(async () => {
        session.hasValidToken = true;
      }),
    };

    return session;
  };

  const at = (query: string) => new URL(`https://epi.example.org/${query}`);

  /** What an author does to reach a label: type something, search, open the one they meant. */
  const searchAndOpen = async () => {
    await userEvent.type(await screen.findByRole('searchbox'), 'Examplinum');
    await userEvent.click(screen.getByRole('button', { name: /^search$/i }));
    await userEvent.click(
      await screen.findByRole('button', { name: /SYNTHETIC TEST LABEL - Examplinum/i }));
  };

  it('puts the label an author opened into the address', async () => {
    // So it can be bookmarked, shared and reloaded. Holding it only in memory made the label an
    // author was working on unaddressable, and a refresh took them back to the search box.
    const pushed: URL[] = [];

    render(
      <App
        session={signedIn}
        platform={platform()}
        location={at('')}
        go={vi.fn()}
        pushAddress={(url) => pushed.push(url)}
      />,
    );

    await searchAndOpen();

    expect(await screen.findByRole('heading', { name: '1. What Examplinum is' })).toBeDefined();
    expect(pushed).toHaveLength(1);
    expect(pushed[0]!.searchParams.get('label')).toBe(version.documentIdentifier);
    expect(pushed[0]!.searchParams.get('version')).toBe('2');
  });

  it('follows the browser back to where the author came from', async () => {
    // Pushed rather than replaced, so back means what it means everywhere else. Without this,
    // back leaves the application and the author loses their place entirely.
    render(
      <App
        session={signedIn}
        platform={platform()}
        location={at('')}
        go={vi.fn()}
        pushAddress={vi.fn()}
      />,
    );

    await searchAndOpen();
    await screen.findByRole('heading', { name: '1. What Examplinum is' });

    // What a browser does when the author presses back: the address changes underneath the page
    // and popstate is the only notice. Here the window's own address names no label, so following
    // it is following the author back to the search they came from.
    window.dispatchEvent(new PopStateEvent('popstate'));

    expect(await screen.findByRole('heading', { name: /find a label/i })).toBeDefined();
  });

  it('remembers where the author was going before sending them to sign in', async () => {
    const session = returning();

    render(
      <App
        session={session}
        platform={platform()}
        location={at(`?label=${version.documentIdentifier}&version=2`)}
        go={vi.fn()}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(session.beginAsync).toHaveBeenCalled());
    expect(String(sessionStorage.getItem('epi.intended'))).toContain(version.documentIdentifier);
  });

  it('opens what the author was sent to, not the search box', async () => {
    // The identity provider sends everybody back to the origin, so the label in the address is
    // gone by the time they return. Somebody following a link from an email landed on a search
    // box with no idea which label they had been sent to.
    sessionStorage.setItem(
      'epi.intended',
      `https://epi.example.org/?label=${version.documentIdentifier}&version=2`);

    render(
      <App
        session={returning()}
        platform={platform()}
        location={at('?code=abc&state=xyz')}
        go={vi.fn()}
        replaceAddress={vi.fn()}
      />,
    );

    expect(await screen.findByRole('heading', { name: '1. What Examplinum is' })).toBeDefined();
  });

  it('does not carry the spent callback parameters into the next address', async () => {
    // Found in a browser, in this change: the cleanup rewrote the address bar and left the state
    // holding the callback's parameters, so the next push put code and state back. An address with
    // a spent code in it is one a refresh replays.
    const pushed: URL[] = [];
    sessionStorage.setItem('epi.intended', 'https://epi.example.org/');

    render(
      <App
        session={returning()}
        platform={platform()}
        location={at('?code=abc&state=xyz')}
        go={vi.fn()}
        replaceAddress={vi.fn()}
        pushAddress={(url) => pushed.push(url)}
      />,
    );

    await searchAndOpen();
    await screen.findByRole('heading', { name: '1. What Examplinum is' });

    expect(pushed).toHaveLength(1);
    expect(pushed[0]!.searchParams.has('code')).toBe(false);
    expect(pushed[0]!.searchParams.has('state')).toBe(false);
    expect(pushed[0]!.searchParams.get('label')).toBe(version.documentIdentifier);
  });

  it('uses a remembered address once and then forgets it', async () => {
    // Otherwise every later sign-in reopens whatever the author was looking at weeks ago, and a
    // shared machine reopens somebody else's label.
    sessionStorage.setItem(
      'epi.intended',
      `https://epi.example.org/?label=${version.documentIdentifier}&version=2`);

    render(
      <App
        session={returning()}
        platform={platform()}
        location={at('?code=abc&state=xyz')}
        go={vi.fn()}
        replaceAddress={vi.fn()}
      />,
    );

    await screen.findByRole('heading', { name: '1. What Examplinum is' });

    expect(sessionStorage.getItem('epi.intended')).toBeNull();
  });
});
