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

/** What an author is looking for. Every part optional, and only what is given is sent. */
export interface SearchCriteria {
  readonly text?: string;
  readonly market?: string;
  readonly state?: string;
  readonly product?: string;
}

/** A product as the directory knows it (ADR-036). */
export interface Product {
  readonly identifier: string;
  readonly name: string;
  readonly markets?: readonly string[];
}

/**
 * Which product a label is about.
 *
 * @remarks
 * The identifier is what the platform stores and resolves; the display is carried for a reader
 * and is never what anything resolves (ADR-040 decision 2).
 */
export interface ProductChoiceValue {
  readonly identifier: string;
  readonly display: string | null;
}

/** One label version the platform is willing to show this caller. */
export interface LabelHit {
  readonly documentIdentifier: string;
  readonly version: number;
  readonly title: string;
  readonly market: string;
  readonly state: string;
}

/**
 * What a search found.
 *
 * @remarks
 * The total is carried because it is not the same as the number of hits, and the difference
 * matters: somebody shown twenty results who assumes that is all of them will conclude a label
 * does not exist. It is also already a scoped total - the platform bounds the query rather than
 * filtering its results (ADR-022 decision 1), so this is a true count of what this caller may
 * see and not of what exists.
 */
export interface SearchResults {
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly hits: readonly LabelHit[];
}

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

  /**
   * Labels this caller may see that match what they are looking for.
   *
   * @remarks
   * Only the criteria that were given are sent. An empty filter sent as an empty string is a
   * filter, and it matches nothing - which would present as "there are no labels" rather than
   * as a mistake.
   */
  async searchLabels(criteria: SearchCriteria): Promise<SearchResults> {
    const query = new URLSearchParams();
    for (const [name, value] of Object.entries(criteria)) {
      if (typeof value === 'string' && value.trim() !== '') {
        query.set(name, value);
      }
    }

    const response = await this.#send(
      `${this.#connection.baseUrl.replace(/\/$/, '')}/labels/search?${query}`,
      { method: 'GET' },
    );

    if (!response.ok) {
      // Reported rather than answered emptily. A failure shown as "no results" is a lie, and
      // the remedy differs entirely.
      throw new Error(
        `The platform answered ${response.status} to that search, so these are not "no results" ` +
          '- the search did not happen.',
      );
    }

    return (await response.json()) as SearchResults;
  }

  /** Products matching what an author is looking for, so one can be chosen rather than typed. */
  async searchProducts(text: string): Promise<readonly Product[]> {
    const response = await this.#send(
      `${this.#connection.baseUrl.replace(/\/$/, '')}/master-data/products` +
        `?text=${encodeURIComponent(text)}`,
      { method: 'GET' },
    );

    if (!response.ok) {
      throw new Error(
        `The platform answered ${response.status} to that product search, so this is not "no ` +
          'products" - the search did not happen.',
      );
    }

    return (await response.json()) as readonly Product[];
  }

  async saveSections(
    documentIdentifier: string,
    version: number,
    sections: readonly SectionDescription[],
    product?: ProductChoiceValue,
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
        // The product is sent only where the author changed it. Omission is not removal, and
        // the platform reads an absent product as unchanged (ADR-040).
        body: JSON.stringify(product === undefined ? { sections } : { sections, product }),
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
