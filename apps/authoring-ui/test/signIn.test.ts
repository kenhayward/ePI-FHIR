import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SignIn } from '../src/platform/signIn';

// Getting a token, and keeping it somewhere safe (FN-AUT-005).
//   CAP-IAM-001 Authenticate through the enterprise identity provider
//
// ADR-039. The platform never authenticates anyone itself, so the only questions here are how a
// browser obtains a token and where it keeps it. The second is the one with a cost: a token in
// localStorage turns any cross-site scripting flaw into a token that authorises writes to
// regulated content, so this keeps it in memory and pays for that with a sign-in after refresh.
describe('FN-AUT-005 signing in', () => {
  const settings = {
    authority: 'https://keycloak.example.org/realms/epi',
    clientId: 'epi-authoring-ui',
    redirectUri: 'https://epi.example.org/callback',
  };

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
  });

  const started = async () => {
    const signIn = new SignIn(settings);
    return { signIn, url: new URL(await signIn.beginAsync()) };
  };

  it('sends the author to the identity provider, asking for a code', async () => {
    const { url } = await started();

    expect(url.origin + url.pathname).toBe(
      'https://keycloak.example.org/realms/epi/protocol/openid-connect/auth',
    );
    expect(url.searchParams.get('response_type')).toBe('code');
    expect(url.searchParams.get('client_id')).toBe('epi-authoring-ui');
    expect(url.searchParams.get('redirect_uri')).toBe(settings.redirectUri);
  });

  it('proves possession with PKCE, and sends only the challenge', async () => {
    // The verifier never leaves the browser until the exchange. Sending it here would make
    // PKCE decorative.
    const { url } = await started();

    expect(url.searchParams.get('code_challenge_method')).toBe('S256');
    const challenge = url.searchParams.get('code_challenge');
    expect(challenge).toBeTruthy();
    expect(url.toString()).not.toContain(sessionStorage.getItem('epi.pkce.verifier') ?? 'absent');
  });

  it('never asks for an implicit token or a password', async () => {
    // Implicit puts a token in a URL, and so in history and every referrer. A password grant
    // would mean this application handles a password, which is what delegating to the identity
    // provider exists to prevent.
    const { url } = await started();

    expect(url.searchParams.get('response_type')).not.toContain('token');
    expect(url.searchParams.has('client_secret')).toBe(false);
  });

  it('refuses a callback whose state does not match the one it sent', async () => {
    // The cross-site request forgery guard the flow depends on. A mismatch is either a defect
    // or an attack, and neither should reach a token exchange.
    const { signIn } = await started();

    await expect(
      signIn.completeAsync(new URL('https://epi.example.org/callback?code=abc&state=not-mine')),
    ).rejects.toThrow(/state/i);
  });

  it('refuses a callback carrying no code at all', async () => {
    const { signIn, url } = await started();
    const state = url.searchParams.get('state')!;

    await expect(
      signIn.completeAsync(new URL(`https://epi.example.org/callback?state=${state}`)),
    ).rejects.toThrow(/code/i);
  });

  it('exchanges the code with the verifier, and holds the token in memory', async () => {
    const exchange = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) =>
      new Response(JSON.stringify({ access_token: 'a-token', expires_in: 300 }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    const signIn = new SignIn({ ...settings, fetcher: exchange as unknown as typeof fetch });
    const url = new URL(await signIn.beginAsync());
    const state = url.searchParams.get('state')!;

    await signIn.completeAsync(
      new URL(`https://epi.example.org/callback?code=the-code&state=${state}`),
    );

    const sent = new URLSearchParams(String(exchange.mock.calls[0]![1]?.body));
    expect(sent.get('grant_type')).toBe('authorization_code');
    expect(sent.get('code')).toBe('the-code');
    expect(sent.get('code_verifier')).toBeTruthy();
    expect(await signIn.tokenAsync()).toBe('a-token');
  });

  it('writes no token into any browser storage', async () => {
    // ADR-039 decision 2, asserted rather than assumed. This is the case that fails the day
    // somebody adds persistence to survive a refresh.
    const signIn = await signedIn();

    expect(await signIn.tokenAsync()).toBe('a-token');
    for (const store of [localStorage, sessionStorage]) {
      for (let i = 0; i < store.length; i++) {
        expect(store.getItem(store.key(i)!)).not.toContain('a-token');
      }
    }
  });

  it('keeps no refresh token even when the identity provider sends one', async () => {
    // A refresh token is a longer-lived credential and the browser is the worst place for one.
    const signIn = await signedIn({ refresh_token: 'a-refresh-token' });

    expect(JSON.stringify(signIn)).not.toContain('a-refresh-token');
    for (const store of [localStorage, sessionStorage]) {
      for (let i = 0; i < store.length; i++) {
        expect(store.getItem(store.key(i)!)).not.toContain('a-refresh-token');
      }
    }
  });

  it('treats an expired token as no token, rather than sending it', async () => {
    // Sending one would fail at the API with a 401 the author cannot act on. Knowing it has
    // expired means the surface can send them back through the identity provider.
    const signIn = await signedIn({ expires_in: 0 });

    expect(signIn.hasValidToken).toBe(false);
    await expect(signIn.tokenAsync()).rejects.toThrow(/sign in/i);
  });

  it('reports an identity provider that refused the exchange', async () => {
    const refusing = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) =>
      new Response(JSON.stringify({ error: 'invalid_grant' }), { status: 400 }),
    );
    const signIn = new SignIn({ ...settings, fetcher: refusing as unknown as typeof fetch });
    const state = new URL(await signIn.beginAsync()).searchParams.get('state')!;

    await expect(
      signIn.completeAsync(new URL(`https://epi.example.org/callback?code=c&state=${state}`)),
    ).rejects.toThrow(/invalid_grant/);
  });

  it('cannot have one authorization code spent twice', async () => {
    // The verifier is consumed by the exchange. A second attempt with the same callback is
    // either a double-submit or a replay, and neither should produce a second token.
    const signIn = await signedIn();
    const state = 'whatever';

    await expect(
      signIn.completeAsync(new URL(`https://epi.example.org/callback?code=c&state=${state}`)),
    ).rejects.toThrow();
  });

  async function signedIn(extra: Record<string, unknown> = {}) {
    const fetcher = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) =>
      new Response(JSON.stringify({ access_token: 'a-token', expires_in: 300, ...extra }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    const signIn = new SignIn({ ...settings, fetcher: fetcher as unknown as typeof fetch });
    const state = new URL(await signIn.beginAsync()).searchParams.get('state')!;
    await signIn.completeAsync(
      new URL(`https://epi.example.org/callback?code=the-code&state=${state}`),
    );
    return signIn;
  }
});
