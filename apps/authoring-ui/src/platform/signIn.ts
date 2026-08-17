/**
 * Signing in to the authoring surface (ADR-039, FN-AUT-005).
 *
 * @remarks
 * <p>
 * Authorization code with PKCE against the identity provider, and nothing else. No secret,
 * because a secret shipped to a browser is not a secret; no implicit flow, because it puts a
 * token in a URL and so in history and every referrer; no password grant, because this
 * application must never handle a password.
 * </p>
 * <p>
 * The access token is held in memory and written nowhere. That costs a sign-in after every page
 * refresh, and it buys the thing that matters: a cross-site scripting flaw on this origin cannot
 * read a token out of storage that authorises writes to regulated content, because there is no
 * storage to read.
 * </p>
 * <p>
 * Implemented directly rather than through a library, deliberately - see ADR-039 decision 5. The
 * dangerous part of OIDC is validating a token, which the API does; this builds a URL, checks a
 * string it generated, and posts a form.
 * </p>
 */
export interface SignInSettings {
  /** The realm's issuer URL, from which the endpoints are derived. */
  readonly authority: string;
  readonly clientId: string;
  readonly redirectUri: string;
  readonly scope?: string;
  readonly fetcher?: typeof fetch;
}

/**
 * Where the one-request-long PKCE secrets live.
 *
 * @remarks
 * Session storage, not memory, because the browser navigates away to the identity provider and
 * back - anything held in a variable is gone by the time the callback arrives. These are not the
 * token: a verifier is useless without the authorization code it was minted for, and it is
 * deleted the moment the code is exchanged.
 */
const VERIFIER = 'epi.pkce.verifier';
const STATE = 'epi.pkce.state';

export class SignIn {
  readonly #settings: SignInSettings;
  readonly #fetch: typeof fetch;

  /** In memory, and deliberately nowhere else (ADR-039 decision 2). */
  #token: string | null = null;
  #expiresAt = 0;

  constructor(settings: SignInSettings) {
    this.#settings = settings;
    // Bound, because it is then called as this object's method. An unbound globalThis.fetch
    // invoked as this.#fetch(...) arrives at the browser with this object as its receiver, and
    // Chrome refuses a Window method called on anything else: "Failed to execute 'fetch' on
    // 'Window': Illegal invocation". Nothing caught it because every test injects a fetcher, and
    // Node's fetch does not check its receiver even when the real one is used.
    this.#fetch = settings.fetcher ?? globalThis.fetch.bind(globalThis);
  }

  #endpoint(name: 'auth' | 'token'): string {
    return `${this.#settings.authority.replace(/\/$/, '')}/protocol/openid-connect/${name}`;
  }

  get hasValidToken(): boolean {
    return this.#token !== null && Date.now() < this.#expiresAt;
  }

  /** The URL to send the author to. */
  async beginAsync(): Promise<string> {
    const verifier = randomString(64);
    const state = randomString(32);

    sessionStorage.setItem(VERIFIER, verifier);
    sessionStorage.setItem(STATE, state);

    const parameters = new URLSearchParams({
      response_type: 'code',
      client_id: this.#settings.clientId,
      redirect_uri: this.#settings.redirectUri,
      scope: this.#settings.scope ?? 'openid profile',
      state,

      // Only the challenge. Sending the verifier here would make PKCE decorative.
      code_challenge: await challengeFor(verifier),
      code_challenge_method: 'S256',
    });

    return `${this.#endpoint('auth')}?${parameters}`;
  }

  /** Handles the redirect back, exchanging the code for a token. */
  async completeAsync(callback: URL): Promise<void> {
    const expected = sessionStorage.getItem(STATE);
    const returned = callback.searchParams.get('state');

    // Refused loudly. A callback whose state does not match is either a defect or a cross-site
    // request forgery, and neither should reach a token exchange (ADR-039 decision 4).
    if (expected === null || returned !== expected) {
      throw new Error(
        'The sign-in state did not match the one this application sent, so the response was ' +
          'refused rather than trusted. Start signing in again.',
      );
    }

    const code = callback.searchParams.get('code');
    if (code === null) {
      throw new Error('The identity provider returned no authorization code.');
    }

    const verifier = sessionStorage.getItem(VERIFIER);
    if (verifier === null) {
      throw new Error(
        'There is no code verifier for this sign-in, so the code cannot be exchanged. An ' +
          'authorization code is spent once.',
      );
    }

    // Consumed before the exchange, so a double submit or a replay of the same callback cannot
    // produce a second token.
    sessionStorage.removeItem(VERIFIER);
    sessionStorage.removeItem(STATE);

    const response = await this.#fetch(this.#endpoint('token'), {
      method: 'POST',
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'authorization_code',
        client_id: this.#settings.clientId,
        redirect_uri: this.#settings.redirectUri,
        code,
        code_verifier: verifier,
      }).toString(),
    });

    const body = (await response.json()) as {
      access_token?: string;
      expires_in?: number;
      error?: string;
      error_description?: string;
    };

    if (!response.ok || body.access_token === undefined) {
      throw new Error(
        `The identity provider refused the sign-in: ${body.error ?? response.status}` +
          (body.error_description === undefined ? '' : ` - ${body.error_description}`),
      );
    }

    this.#token = body.access_token;

    // A minute short, so a request begun just before expiry does not arrive just after it.
    this.#expiresAt = Date.now() + Math.max(0, (body.expires_in ?? 300) - 60) * 1000;

    // Any refresh token the provider sent is deliberately not kept (ADR-039 decision 3).
  }

  /** The access token, for the platform client to send. */
  async tokenAsync(): Promise<string> {
    if (!this.hasValidToken) {
      throw new Error('Your sign-in has expired. Sign in again to continue.');
    }

    return this.#token!;
  }
}

const randomString = (bytes: number): string =>
  base64Url(crypto.getRandomValues(new Uint8Array(bytes)));

const challengeFor = async (verifier: string): Promise<string> =>
  base64Url(
    new Uint8Array(
      await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier)),
    ),
  );

const base64Url = (bytes: Uint8Array): string =>
  btoa(String.fromCharCode(...bytes))
    .replaceAll('+', '-')
    .replaceAll('/', '_')
    .replaceAll('=', '');
