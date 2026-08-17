import { describe, expect, it } from 'vitest';
import { PlatformClient } from '../src/platform/client';
import { loadSettings } from '../src/platform/settings';
import { SignIn } from '../src/platform/signIn';

// The browser's fetch, invoked the way a browser requires (FN-AUT-018).
//   CAP-IAM-001 Authenticate via the enterprise identity provider
//
// A defect found by opening the application, and invisible to every test that existed:
//
//   Failed to execute 'fetch' on 'Window': Illegal invocation
//
// `this.#fetch = globalThis.fetch` stores the function unbound, and `this.#fetch(...)` then calls
// it with the surrounding object as its receiver. Chrome checks the receiver on a Window method
// and refuses. Nothing caught it because every test injects its own fetcher, so the branch that
// takes the global one was never executed - and Node's fetch does not check its receiver even
// when it is, so injecting the real one would not have caught it either.
//
// The tests below check the binding rather than the symptom, with a fetch that refuses a wrong
// receiver exactly as a browser's does. Anything that stores an unbound global fetch fails them.
describe('FN-AUT-018 calling the browser fetch', () => {
  /**
   * A stand-in for the browser's fetch: it refuses any receiver that is not the global object,
   * which is what Chrome does and what Node does not.
   */
  const browserLike = function (this: unknown): Promise<Response> {
    if (this !== undefined && this !== globalThis) {
      throw new TypeError("Failed to execute 'fetch' on 'Window': Illegal invocation");
    }

    return Promise.resolve(
      new Response(JSON.stringify({ access_token: 'a-token', expires_in: 300 }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
  };

  /**
   * Why this fails on success as well as on failure: an assertion inside a .catch that never runs
   * is an assertion that never happened, and a test which passes because the call succeeded is
   * indistinguishable from one that passes because the call was correct.
   */
  const failureOf = async (call: () => Promise<unknown>): Promise<string> =>
    call().then(() => '', (failed: Error) => failed.message);

  /** Installs it as the global fetch for one test, and puts back what was there. */
  const withBrowserLikeFetch = async (body: () => Promise<void>) => {
    const original = globalThis.fetch;
    globalThis.fetch = browserLike as unknown as typeof fetch;

    try {
      await body();
    } finally {
      globalThis.fetch = original;
    }
  };

  it('signs in without an illegal invocation', async () =>
    withBrowserLikeFetch(async () => {
      const signIn = new SignIn({
        authority: 'http://localhost:8081/realms/epi',
        clientId: 'epi-authoring-ui',
        redirectUri: 'http://localhost:5173/',
      });

      // Begun for real, so the state and verifier the callback is checked against are the ones
      // this instance wrote. A callback with a made-up state is refused before the token
      // exchange, and a test that stopped there would never reach the call that was broken.
      const authorize = new URL(await signIn.beginAsync());
      const state = authorize.searchParams.get('state')!;

      expect(await failureOf(() =>
        signIn.completeAsync(new URL(`http://localhost:5173/?code=abc&state=${state}`))))
        .not.toMatch(/illegal invocation/i);
    }));

  it('reaches the platform without an illegal invocation', async () =>
    withBrowserLikeFetch(async () => {
      const client = new PlatformClient({
        baseUrl: 'http://localhost:8080',
        token: async () => 'a-token',
      });

      expect(await failureOf(() => client.openTasks())).not.toMatch(/illegal invocation/i);
    }));

  it('reads its configuration without an illegal invocation', async () =>
    withBrowserLikeFetch(async () => {
      // This one already worked, because a plain call leaves the receiver undefined rather than
      // wrong. Asserted anyway: it is the same hazard one refactor away, and the refactor that
      // introduces it would be moving the call onto an object.
      expect(await failureOf(() => loadSettings())).not.toMatch(/illegal invocation/i);
    }));
});
