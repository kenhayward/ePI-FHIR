import type { SectionDescription, VersionDescription } from '../authoring/editingSession';

/**
 * How the client reaches the platform.
 *
 * @remarks
 * The token is asked for per request rather than held, because an access token expires and a
 * client holding one from page load would start failing silently partway through an editing
 * session. How one is obtained is the identity provider's business and not this module's
 * (ADR-037: the platform never authenticates anyone itself).
 */
export interface PlatformConnection {
  readonly baseUrl: string;
  readonly token: () => Promise<string>;
  readonly fetcher?: typeof fetch;
}

/**
 * What a save did, or what stopped it.
 *
 * @remarks
 * The kinds are separate because their remedies are: sign in again, ask for access, fix the
 * content, reload and look at what somebody else wrote, or simply try again. Collapsing them
 * into one failure makes every one of them unactionable, which is the usual way a save button
 * becomes something people stop trusting.
 */
export type SaveOutcome =
  | { readonly ok: true; readonly version: number }
  | { readonly ok: false; readonly kind: 'refused'; readonly problems: readonly string[] }
  | { readonly ok: false; readonly kind: 'unauthenticated' }
  | { readonly ok: false; readonly kind: 'forbidden'; readonly detail: string }
  | { readonly ok: false; readonly kind: 'conflict'; readonly detail: string }
  | { readonly ok: false; readonly kind: 'unreachable'; readonly detail: string };

/**
 * The surface's only way to reach the platform (ADR-038's endpoints, FN-AUT-004).
 *
 * @remarks
 * It speaks sections, never FHIR, because that is what the platform offers it (ADR-037
 * decision 2). Nothing here decides anything: it carries what the platform said, including its
 * refusals, in the platform's own words.
 */
export class PlatformClient {
  readonly #connection: PlatformConnection;
  readonly #fetch: typeof fetch;

  /**
   * The sections the platform last sent, per version.
   *
   * @remarks
   * Kept so a save can refuse a section that was never loaded. The platform refuses one too
   * (ADR-038 decision 4) - this is the near side of the same invariant, and it turns a round
   * trip into an immediate error with the identity named.
   */
  readonly #loaded = new Map<string, ReadonlySet<string>>();

  constructor(connection: PlatformConnection) {
    this.#connection = connection;
    this.#fetch = connection.fetcher ?? globalThis.fetch;
  }

  async loadVersion(documentIdentifier: string, version: number): Promise<VersionDescription> {
    const response = await this.#send(this.#url(documentIdentifier, version), { method: 'GET' });

    if (response.status === 404) {
      throw new Error(
        `Version ${version} of that label was not found. It may never have existed, or it may ` +
          'be outside what you are allowed to see - the platform does not distinguish the two, ' +
          'deliberately.',
      );
    }

    if (!response.ok) {
      throw new Error(`The platform answered ${response.status} for that version.`);
    }

    const described = (await response.json()) as VersionDescription;
    this.#loaded.set(
      this.#key(documentIdentifier, version),
      new Set(described.sections.map((section) => section.identity)),
    );

    return described;
  }

  async saveSections(
    documentIdentifier: string,
    version: number,
    sections: readonly SectionDescription[],
  ): Promise<SaveOutcome> {
    const known = this.#loaded.get(this.#key(documentIdentifier, version));
    const invented = sections.find(
      (section) => known !== undefined && !known.has(section.identity),
    );

    if (invented !== undefined) {
      throw new Error(
        `Section '${invented.identity}' was not part of the version that was loaded, so saving ` +
          'it would be asking for a section to be invented. Adding a section is a separate ' +
          'operation with its own rules (ADR-038).',
      );
    }

    let response: Response;
    try {
      response = await this.#send(this.#url(documentIdentifier, version), {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ sections }),
      });
    } catch (unreachable) {
      // Nothing was decided, so nothing was lost and retrying is safe. Worth saying, because
      // the alternative is an author assuming their work went somewhere.
      return {
        ok: false,
        kind: 'unreachable',
        detail: unreachable instanceof Error ? unreachable.message : String(unreachable),
      };
    }

    if (response.ok) {
      const created = (await response.json()) as { version: number };
      return { ok: true, version: created.version };
    }

    const body = (await this.#body(response)) as { problems?: string[]; detail?: string };

    switch (response.status) {
      case 401:
        return { ok: false, kind: 'unauthenticated' };
      case 403:
        return { ok: false, kind: 'forbidden', detail: body.detail ?? 'Access was denied.' };
      case 409:
        return {
          ok: false,
          kind: 'conflict',
          detail: body.detail ?? 'Another version was created while this one was being edited.',
        };
      default:
        return {
          ok: false,
          kind: 'refused',

          // The platform's own words. Rewriting them here would mean an author reading a
          // paraphrase of a validation failure they then cannot find in the content.
          problems: body.problems ?? [`The platform answered ${response.status}.`],
        };
    }
  }

  #url(documentIdentifier: string, version: number): string {
    return (
      `${this.#connection.baseUrl.replace(/\/$/, '')}` +
      `/labels/${encodeURIComponent(documentIdentifier)}/versions/${version}/sections`
    );
  }

  #key(documentIdentifier: string, version: number): string {
    return `${documentIdentifier}@${version}`;
  }

  async #send(url: string, init: RequestInit): Promise<Response> {
    const token = await this.#connection.token();

    // In the header, never in the URL: a token in a query string is a token in every access
    // log and every referrer header.
    return this.#fetch(url, {
      ...init,
      headers: { ...init.headers, authorization: `Bearer ${token}` },
    });
  }

  async #body(response: Response): Promise<unknown> {
    try {
      return await response.json();
    } catch {
      return {};
    }
  }
}
