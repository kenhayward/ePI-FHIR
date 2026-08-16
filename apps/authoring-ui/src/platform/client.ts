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

/** Something routing has asked this caller to do (ADR-031). */
export interface WaitingTask {
  readonly identifier: string;
  readonly documentIdentifier: string;
  readonly version: number;
  readonly action: string;
  readonly assignee: string;
  readonly raisedAt: string;
}

/**
 * What a signature asks for.
 *
 * @remarks
 * The password is here because signing a record is not signing in (ADR-041 decision 1). Part 11
 * requires the signer to supply identification components at the moment of signing; a signature
 * on the session alone is an assertion by a browser tab. It exists for one request and is never
 * held (ADR-041 decision 2).
 */
export interface SignatureRequest {
  readonly documentIdentifier: string;
  readonly version: number;
  readonly meaning: string;
  readonly password: string;
  readonly reason?: string;
}

/** A signature the platform minted, or its refusal. */
export type SignatureOutcome =
  | {
      readonly refused: false;
      readonly reference: string;
      readonly printedName?: string | undefined;
    }
  | { readonly refused: true; readonly detail: string };

/** What a transition asks for. */
export interface TransitionRequest {
  readonly action: string;
  readonly reason?: string;
  readonly signatureReference?: string;
}

/** What the platform did with a transition, or why it would not. */
export type TransitionOutcome =
  | { readonly ok: true; readonly from: string; readonly to: string }
  | { readonly ok: false; readonly detail: string };

/** Where each market stands for a version, as the platform reports it (ADR-005). */
export interface MarketStandings {
  readonly marketActions: Readonly<
    Record<
      string,
      {
        readonly state: string;
        readonly actions: readonly string[];
        readonly signedActions: readonly string[];
        readonly actionsNeedingEffectiveDate: readonly string[];
        readonly signatureMeanings: Readonly<Record<string, string>>;
      }
    >
  >;
}

/**
 * What the platform actually sends: where each market stands, and what may be done about each,
 * as two fields answering two questions.
 */
interface StateResponse {
  readonly markets?: Readonly<Record<string, string>>;
  readonly marketActions?: Readonly<
    Record<
      string,
      {
        readonly actions?: readonly string[];
        readonly signedActions?: readonly string[];
        readonly actionsNeedingEffectiveDate?: readonly string[];
        readonly signatureMeanings?: Readonly<Record<string, string>>;
      }
    >
  >;
}

/** What happened to a version, and what it was approved against (ADR-023). */
export interface VersionRecord {
  readonly state: string;
  readonly author: string | null;
  readonly contentHash: string;
  readonly packagesStillMatch: boolean;
  readonly pinnedContext: {
    readonly packages: readonly { readonly name: string; readonly version: string }[];
    readonly terminologyBindings: readonly {
      readonly system: string;
      readonly version: string | null;
      readonly isVersioned: boolean;
    }[];
  } | null;
  readonly history: readonly {
    readonly from: string;
    readonly to: string;
    readonly action: string;
    readonly actor: string;
    readonly at: string;
    readonly signature: {
      readonly printedName: string;
      readonly meaning: string;
      readonly contentHash: string;
      readonly signedAt: string;
    } | null;
  }[];
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

  /**
   * What routing has asked this caller to do.
   *
   * @remarks
   * A failure is never an empty list. Nothing waiting is a claim - it tells somebody their work
   * is done - and reporting a failure as one would say that when nobody knows.
   */
  async openTasks(): Promise<readonly WaitingTask[]> {
    const response = await this.#send(
      `${this.#connection.baseUrl.replace(/\/$/, '')}/tasks`, { method: 'GET' });

    if (!response.ok) {
      throw new Error(
        `The platform answered ${response.status} when asked what is waiting for you, so this ` +
          'is not "nothing waiting" - it was not answered.',
      );
    }

    return (await response.json()) as readonly WaitingTask[];
  }

  /**
   * Asks the platform for a signature over a version.
   *
   * @remarks
   * To the platform, never to the identity provider (ADR-041 decision 3): a browser posting
   * credentials straight to Keycloak would be a second authentication path with none of the
   * platform's segregation-of-duties checks around it.
   */
  async signAsync(request: SignatureRequest): Promise<SignatureOutcome> {
    const response = await this.#send(
      `${this.#connection.baseUrl.replace(/\/$/, '')}/signatures`,
      {
        method: 'POST',
        headers: { 'content-type': 'application/json' },

        // In the body. A password in a URL is a password in every access log and referrer
        // header - the same rule the access token follows, and more so.
        body: JSON.stringify(request),
      },
    );

    const body = (await this.#body(response)) as {
      reference?: string;
      printedName?: string;
      detail?: string;
    };

    if (!response.ok || body.reference === undefined) {
      return {
        refused: true,
        detail: body.detail ?? `The platform answered ${response.status}.`,
      };
    }

    return { refused: false, reference: body.reference, printedName: body.printedName };
  }

  /**
   * Moves a version between states, citing a signature where the gate needs one.
   *
   * @remarks
   * The surface never decides whether a transition may happen and never mints a signature
   * reference: it cites one, and the platform decides whether it is valid, unspent and over the
   * right content hash (ADR-041 decision 4). Being wrong here is refused rather than admitted.
   */
  async transitionAsync(
    documentIdentifier: string,
    version: number,
    request: TransitionRequest,
  ): Promise<TransitionOutcome> {
    const response = await this.#send(
      `${this.#connection.baseUrl.replace(/\/$/, '')}` +
        `/labels/${encodeURIComponent(documentIdentifier)}/versions/${version}/transitions`,
      {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(request),
      },
    );

    const body = (await this.#body(response)) as {
      from?: string;
      to?: string;
      detail?: string;
      problems?: string[];
    };

    if (!response.ok || body.to === undefined) {
      // The platform's own reason. Its refusals are the interesting part - the author of a
      // version may not approve it - and being told exactly that is the point.
      return {
        ok: false,
        detail:
          body.detail
          ?? body.problems?.join('; ')
          ?? `The platform answered ${response.status}.`,
      };
    }

    return { ok: true, from: body.from ?? '', to: body.to };
  }

  /**
   * Where each market stands, and what may be done about each.
   *
   * @remarks
   * A separate read from the authoring projection, because per-market approval is held
   * separately from internal lifecycle on purpose (ADR-005) and joining them here would be the
   * first step towards joining them everywhere.
   */
  async marketStandingsAsync(
    documentIdentifier: string,
    version: number,
  ): Promise<MarketStandings> {
    const response = await this.#send(
      `${this.#connection.baseUrl.replace(/\/$/, '')}` +
        `/labels/${encodeURIComponent(documentIdentifier)}/versions/${version}/state`,
      { method: 'GET' },
    );

    if (!response.ok) {
      throw new Error(
        `The platform answered ${response.status} for that version's markets, so this is not ` +
          '"no markets" - it was not answered.',
      );
    }

    // Joined here rather than on the wire. The platform answers "where does each market stand"
    // and "what may be done about it" separately, because the first is a shape callers already
    // read and answering a second question by changing the first would break every one of them.
    // A market with a state and no actions is a market with nothing to do, not a missing one.
    const body = (await response.json()) as StateResponse;

    return {
      marketActions: Object.fromEntries(
        Object.entries(body.markets ?? {}).map(([market, state]) => [
          market,
          {
            state,
            actions: body.marketActions?.[market]?.actions ?? [],
            signedActions: body.marketActions?.[market]?.signedActions ?? [],
            actionsNeedingEffectiveDate:
              body.marketActions?.[market]?.actionsNeedingEffectiveDate ?? [],
            signatureMeanings: body.marketActions?.[market]?.signatureMeanings ?? {},
          },
        ]),
      ),
    };
  }

  /** Moves one market's approval state, which is never the internal lifecycle's. */
  async marketTransitionAsync(
    documentIdentifier: string,
    version: number,
    market: string,
    request: {
      action: string;
      reason?: string;
      signatureReference?: string;
      effectiveFrom?: string;
    },
  ): Promise<TransitionOutcome> {
    const response = await this.#send(
      `${this.#connection.baseUrl.replace(/\/$/, '')}` +
        `/labels/${encodeURIComponent(documentIdentifier)}/versions/${version}` +
        `/markets/${encodeURIComponent(market)}/transitions`,
      {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(request),
      },
    );

    const body = (await this.#body(response)) as {
      from?: string;
      to?: string;
      detail?: string;
      problems?: string[];
    };

    if (!response.ok || body.to === undefined) {
      return {
        ok: false,
        detail:
          body.detail
          ?? body.problems?.join('; ')
          ?? `The platform answered ${response.status}.`,
      };
    }

    return { ok: true, from: body.from ?? '', to: body.to };
  }

  /**
   * What happened to a version, and what it was approved against.
   *
   * @remarks
   * Derived by the platform from its append-only history, never assembled here - the whole
   * value of it is that it is the record rather than a reading of it (ADR-023).
   */
  async versionRecordAsync(
    documentIdentifier: string,
    version: number,
  ): Promise<VersionRecord> {
    const response = await this.#send(
      `${this.#connection.baseUrl.replace(/\/$/, '')}` +
        `/labels/${encodeURIComponent(documentIdentifier)}/versions/${version}/reconstruction`,
      { method: 'GET' },
    );

    if (!response.ok) {
      throw new Error(
        `The platform answered ${response.status} for that version's history, so this is not ` +
          '"nothing happened" - it was not read.',
      );
    }

    const body = (await response.json()) as VersionRecord & {
      pinnedContext: (VersionRecord['pinnedContext'] & object) | null;
    };

    return {
      ...body,
      pinnedContext:
        body.pinnedContext === null
          ? null
          : {
              packages: body.pinnedContext.packages ?? [],

              // Absent for a pin taken before terminology was recorded, which is not the same
              // as an approval that was asked and had none (ADR-036 decision 3). Both read as
              // empty here; the distinction lives in the record rather than on the screen.
              terminologyBindings: body.pinnedContext.terminologyBindings ?? [],
            },
    };
  }

  /**
   * The leaflet a version produces, as HTML.
   *
   * @remarks
   * Text rather than JSON, because it is a document. It goes into a sandboxed frame and never
   * into the application's own page.
   */
  async previewAsync(documentIdentifier: string, version: number): Promise<string> {
    const response = await this.#send(
      `${this.#connection.baseUrl.replace(/\/$/, '')}` +
        `/labels/${encodeURIComponent(documentIdentifier)}/versions/${version}/preview`,
      { method: 'GET' },
    );

    if (!response.ok) {
      throw new Error(
        `The platform answered ${response.status} for that preview, so this is not an empty ` +
          'leaflet - it was not rendered.',
      );
    }

    return response.text();
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
