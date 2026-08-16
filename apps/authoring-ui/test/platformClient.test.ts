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

  it('searches for labels, and reports how many there are in total', async () => {
    // The total matters as much as the hits. Somebody shown twenty results who assumes that is
    // all of them will conclude a label does not exist.
    const fetcher = respondWith(200, {
      total: 34,
      page: 1,
      pageSize: 20,
      hits: [{ documentIdentifier: 'doc-1', version: 2, title: 'A leaflet', market: 'GB', state: 'approved' }],
    });

    const found = await clientOver(fetcher as unknown as typeof fetch).searchLabels({
      text: 'examplinum',
    });

    expect(found.total).toBe(34);
    expect(found.pageSize).toBe(20);
    expect(found.hits[0]!.title).toBe('A leaflet');
  });

  it('sends only the criteria that were given', async () => {
    // An empty filter sent as an empty string is a filter, and it matches nothing.
    const fetcher = respondWith(200, { total: 0, page: 1, pageSize: 20, hits: [] });

    await clientOver(fetcher as unknown as typeof fetch).searchLabels({ text: 'x', market: 'GB' });

    const query = new URL(String(fetcher.mock.calls[0]![0])).searchParams;
    expect(query.get('text')).toBe('x');
    expect(query.get('market')).toBe('GB');
    expect(query.has('state')).toBe(false);
  });

  it('reports a search nobody is allowed to make, rather than answering it emptily', async () => {
    // An error shown as "no results" is a lie, and the remedy differs entirely.
    const client = clientOver(respondWith(403, {}) as unknown as typeof fetch);

    await expect(client.searchLabels({ text: 'x' })).rejects.toThrow(/403|not allowed/i);
  });

  it('sends a product only where the author changed one', async () => {
    // Omission is not removal: an absent product means unchanged, so sending the current one
    // back would say nothing and sending null would detach the label from it (ADR-040).
    const fetcher = respondWith(201, { version: 3 });
    const client = clientOver(fetcher as unknown as typeof fetch);

    await client.saveSections('doc-1', 2, []);
    expect(JSON.parse(String(fetcher.mock.calls[0]![1]?.body))).not.toHaveProperty('product');

    await client.saveSections('doc-1', 2, [], { identifier: 'PROD-0001', display: 'A product' });
    expect(JSON.parse(String(fetcher.mock.calls[1]![1]?.body)).product).toEqual({
      identifier: 'PROD-0001',
      display: 'A product',
    });
  });

  it('asks what is waiting for the author', async () => {
    const fetcher = respondWith(200, [
      {
        identifier: 'task-1',
        documentIdentifier: 'doc-1',
        version: 2,
        action: 'approve',
        assignee: 'approver',
        raisedAt: '2026-08-16T09:00:00Z',
      },
    ]);

    const waiting = await clientOver(fetcher as unknown as typeof fetch).openTasks();

    expect(waiting[0]!.action).toBe('approve');
    expect(String(fetcher.mock.calls[0]![0])).toContain('/tasks');
  });

  it('reports a task list that could not be fetched, rather than an empty one', async () => {
    // An empty list means nothing is waiting, which is a claim. A failure that presented as one
    // would tell somebody their work is done when nobody knows.
    const client = clientOver(respondWith(503, {}) as unknown as typeof fetch);

    await expect(client.openTasks()).rejects.toThrow(/503/);
  });

  it('asks the platform for a signature, and never the identity provider', async () => {
    // ADR-041 decision 3. A browser posting credentials straight to Keycloak would be a second
    // authentication path with none of the platform's segregation-of-duties checks around it.
    const fetcher = respondWith(200, { reference: 'sig-1', printedName: 'Ben Okafor' });

    const signature = await clientOver(fetcher as unknown as typeof fetch).signAsync({
      documentIdentifier: 'doc-1',
      version: 2,
      meaning: 'Approval',
      password: 'a-password',
    });

    expect(signature.refused).toBe(false);
    expect(signature.refused === false && signature.reference).toBe('sig-1');
    expect(String(fetcher.mock.calls[0]![0])).toContain('/signatures');
    expect(String(fetcher.mock.calls[0]![0])).not.toContain('keycloak');
  });

  it('never puts the password in the address', async () => {
    // A password in a URL is a password in every access log and referrer header - the same rule
    // the access token follows, and more so.
    const fetcher = respondWith(200, { reference: 'sig-1' });

    await clientOver(fetcher as unknown as typeof fetch).signAsync({
      documentIdentifier: 'doc-1',
      version: 2,
      meaning: 'Approval',
      password: 'a-password',
    });

    expect(String(fetcher.mock.calls[0]![0])).not.toContain('a-password');
  });

  it('says a signature was refused, rather than reporting it as some other failure', async () => {
    // A wrong password and an unreachable platform have entirely different remedies, and the
    // first is the one somebody can do something about immediately.
    const refused = await clientOver(
      respondWith(401, { detail: 'those credentials were not accepted' }) as unknown as typeof fetch,
    ).signAsync({ documentIdentifier: 'doc-1', version: 2, meaning: 'Approval', password: 'wrong' });

    expect(refused).toMatchObject({ refused: true });
  });

  it('moves a version between states, citing the signature that opened the gate', async () => {
    const fetcher = respondWith(200, { from: 'in-review', to: 'approved' });

    const moved = await clientOver(fetcher as unknown as typeof fetch).transitionAsync(
      'doc-1', 2, { action: 'approve', reason: 'reviewed', signatureReference: 'sig-1' });

    expect(moved).toMatchObject({ ok: true });
    const sent = JSON.parse(String(fetcher.mock.calls[0]![1]?.body));
    expect(sent.signatureReference).toBe('sig-1');
  });

  it('hands back the platform reason when a transition is refused', async () => {
    // The gate is the control and its refusals are the interesting part: the author of a
    // version may not approve it, and being told exactly that is the whole point.
    const refused = await clientOver(
      respondWith(409, { detail: 'the author of a version may not approve it.' }) as unknown as typeof fetch,
    ).transitionAsync('doc-1', 2, { action: 'approve' });

    expect(refused).toMatchObject({
      ok: false,
      detail: 'the author of a version may not approve it.',
    });
  });

  it('joins where each market stands with what may be done about it', async () => {
    // The platform answers those as two fields, because the first is a shape callers already
    // read. Joining them here rather than on the wire is what keeps that true - and getting it
    // wrong would render a market with no state at all.
    const fetcher = respondWith(200, {
      markets: { GB: 'not-submitted', DE: 'under-assessment' },
      marketActions: {
        GB: { actions: ['submit'], signedActions: ['submit'], actionsNeedingEffectiveDate: [] },
        DE: {
          actions: ['record-approval'],
          signedActions: [],
          actionsNeedingEffectiveDate: ['record-approval'],
        },
      },
    });

    const standing = await clientOver(fetcher as unknown as typeof fetch)
      .marketStandingsAsync('doc-1', 2);

    expect(standing.marketActions['GB']).toEqual({
      state: 'not-submitted',
      actions: ['submit'],
      signedActions: ['submit'],
      actionsNeedingEffectiveDate: [],
    });
    expect(standing.marketActions['DE']!.state).toBe('under-assessment');
  });

  it('reports a market with a state and no actions as one with nothing to do', async () => {
    // Rather than dropping it. A market missing from a screen is one somebody assumes is fine.
    const fetcher = respondWith(200, { markets: { EU: 'withdrawn' }, marketActions: {} });

    const standing = await clientOver(fetcher as unknown as typeof fetch)
      .marketStandingsAsync('doc-1', 2);

    expect(standing.marketActions['EU']).toEqual({
      state: 'withdrawn',
      actions: [],
      signedActions: [],
      actionsNeedingEffectiveDate: [],
    });
  });
});
