import { useCallback, useEffect, useState } from 'react';
import { LabelEditor } from './LabelEditor';
import { LabelPicker } from './LabelPicker';
import { LeafletPreview } from './LeafletPreview';
import { OfficialLeaflet } from './OfficialLeaflet';
import { ProductChoice } from './ProductChoice';
import { LifecycleActions } from './LifecycleActions';
import { MarketApprovals } from './MarketApprovals';
import { VersionHistory } from './VersionHistory';
import { WaitingWork } from './WaitingWork';
import type { SectionDescription, VersionDescription } from './authoring/editingSession';
import type {
  FiledRender,
  MarketStandings,
  OfficialRenderOutcome,
  Product,
  RenderTemplateChoice,
  VersionRecord,
  SignatureOutcome,
  TransitionOutcome,
  TransitionRequest,
  WaitingTask,
  ProductChoiceValue,
  SaveOutcome,
  SearchCriteria,
  SearchResults,
} from './platform/client';

/**
 * What the application needs of a sign-in, and of the platform.
 *
 * @remarks
 * Named as the narrow things they are rather than as the concrete classes, so this can be tested
 * without a browser redirect or a network - and so the parts stay replaceable, which is what
 * ADR-039 decision 5 says will matter if the identity story grows.
 */
export interface Session {
  readonly hasValidToken: boolean;
  beginAsync(): Promise<string>;
  completeAsync(callback: URL): Promise<void>;
}

export interface Platform {
  loadVersion(documentIdentifier: string, version: number): Promise<VersionDescription>;
  saveSections(
    documentIdentifier: string,
    version: number,
    sections: readonly SectionDescription[],
    product?: ProductChoiceValue,
  ): Promise<SaveOutcome>;
  searchLabels(criteria: SearchCriteria): Promise<SearchResults>;
  searchProducts(text: string): Promise<readonly Product[]>;
  openTasks(): Promise<readonly WaitingTask[]>;
  transitionAsync(
    documentIdentifier: string,
    version: number,
    request: TransitionRequest,
  ): Promise<TransitionOutcome>;
  signAsync(request: {
    documentIdentifier: string;
    version: number;
    meaning: string;
    password: string;
  }): Promise<SignatureOutcome>;
  marketStandingsAsync(documentIdentifier: string, version: number): Promise<MarketStandings>;
  versionRecordAsync(documentIdentifier: string, version: number): Promise<VersionRecord>;
  previewAsync(documentIdentifier: string, version: number): Promise<string>;
  approvedTemplatesAsync(): Promise<readonly RenderTemplateChoice[]>;
  filedRendersAsync(documentIdentifier: string, version: number): Promise<readonly FiledRender[]>;
  produceRenderAsync(
    documentIdentifier: string,
    version: number,
    template: string,
  ): Promise<OfficialRenderOutcome>;
  filedRenderAsync(
    documentIdentifier: string,
    version: number,
    template: string,
    templateVersion: number,
  ): Promise<string>;
  marketTransitionAsync(
    documentIdentifier: string,
    version: number,
    market: string,
    request: {
      action: string;
      reason?: string;
      signatureReference?: string;
      effectiveFrom?: string;
    },
  ): Promise<TransitionOutcome>;
}

/**
 * The authoring application: sign in, open a label, edit it, save the next version.
 *
 * @remarks
 * Nothing here decides anything about the content or who may change it (ADR-037 decision 1).
 * What it is responsible for is that the author is never left looking at a screen that does not
 * say what happened - which is why most of what follows is about outcomes rather than about
 * editing.
 */
export function App({
  session,
  platform,
  location,
  go,
  replaceAddress = (url) => window.history.replaceState(null, '', url.toString()),
  pushAddress = (url) => window.history.pushState(null, '', url.toString()),
}: {
  readonly session: Session;
  readonly platform: Platform;
  readonly location: URL;
  readonly go: (url: string) => void;

  /**
   * Rewrites the address without navigating, so a spent authorization code can be taken out of
   * it. Injectable because a test has no history to inspect, and defaulted because every caller
   * in a browser wants the same thing.
   */
  readonly replaceAddress?: (url: URL) => void;

  /**
   * Adds an address to the history, so the label an author opened can be bookmarked, shared and
   * reloaded - and so the browser's back button means what it means everywhere else.
   */
  readonly pushAddress?: (url: URL) => void;
}) {
  const [version, setVersion] = useState<VersionDescription | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<SaveOutcome | null>(null);
  const [standings, setStandings] = useState<MarketStandings | null>(null);

  /**
   * Where the author is.
   *
   * @remarks
   * Held in state rather than read from the prop, because the address changes while the
   * application runs: opening a label pushes one, and the browser's back button replaces one
   * underneath us. It used to be the prop plus a separate note of what had been picked, which
   * meant the label an author was working on was nowhere in the address - unbookmarkable,
   * unshareable, and lost on a refresh.
   *
   * A remembered address wins on the way back from the identity provider. Everybody is sent back
   * to the origin, so the label somebody followed a link to is gone from the address by the time
   * they return, and they landed on a search box with no idea which label they had been sent.
   */
  const [address, setAddress] = useState<URL>(() => intendedInsteadOf(location));

  // Undefined until the author changes it, and sent that way: omission is not removal, and the
  // platform reads an absent product as unchanged (ADR-040). Sending the current one back
  // unchanged would say nothing; sending null would detach the label from it.
  const [product, setProduct] = useState<ProductChoiceValue | undefined>(undefined);

  /**
   * How many sign-ins have completed. Its value means nothing; changing it is what makes React
   * look at session.hasValidToken again.
   */
  const [signIns, setSignIns] = useState(0);

  const fromAddress = address.searchParams.get('label');
  const wanted = {
    label: fromAddress,
    version: fromAddress === null ? 0 : Number(address.searchParams.get('version') ?? '0'),
  };

  const returning = address.searchParams.has('code');

  /** Opening a label: into the address, and into the history so back works. */
  const open = useCallback(
    (label: string, version: number) => {
      const opened = new URL(address.href);
      opened.searchParams.set('label', label);
      opened.searchParams.set('version', String(version));
      opened.searchParams.delete('view');

      setAddress(opened);

      try {
        pushAddress(opened);
      } catch {
        // The label is open either way. Some environments refuse pushState, and declining to open
        // a label because the address bar could not be updated would be losing the thing for the
        // label on it.
      }
    },
    [address, pushAddress],
  );

  // The browser's back and forward buttons change the address underneath this page and tell
  // nobody. Without this, back leaves the application rather than returning the author to the
  // search they came from.
  useEffect(() => {
    const followed = () => setAddress(new URL(window.location.href));

    window.addEventListener('popstate', followed);
    return () => window.removeEventListener('popstate', followed);
  }, []);

  useEffect(() => {
    if (!returning || session.hasValidToken) {
      return;
    }

    session
      .completeAsync(address)
      .then(() => {
        // Counted, because hasValidToken is a getter on a mutable object and nothing else tells
        // React the answer has changed. Without this the exchange succeeded, the token sat in
        // memory, and the author went on being shown "You are signed out" - found by signing in
        // through a browser, and invisible to a test that only asserted completeAsync was called.
        setSignIns((count) => count + 1);

        // The spent code taken out of the address. An authorization code is used once (ADR-039
        // decision 4), so leaving it there means a refresh replays a code the identity provider
        // has already consumed and the author is shown a state-mismatch refusal for pressing F5.
        const settled = new URL(address.href);
        for (const spent of ['code', 'state', 'session_state', 'iss']) {
          settled.searchParams.delete(spent);
        }

        // Into the state as well as the bar. Rewriting only the bar left this component holding
        // the callback's parameters, and the next address it pushed put the spent code back -
        // found in a browser, in the change that introduced it.
        setAddress(settled);

        try {
          replaceAddress(settled);
        } catch {
          // Swallowed on purpose, and separately from the exchange. Tidying the address is
          // cosmetic; some environments refuse replaceState outright, and reporting that as a
          // problem would tell an author their sign-in failed when it had just succeeded.
        }
      })
      .catch((failed: Error) => setProblem(failed.message));
  }, [returning, session, address, replaceAddress]);

  useEffect(() => {
    if (!session.hasValidToken || wanted.label === null || wanted.version < 1) {
      return;
    }

    platform
      .loadVersion(wanted.label, wanted.version)
      .then(setVersion)
      .catch((failed: Error) => setProblem(failed.message));

    // A separate read, because per-market approval is held separately from internal lifecycle
    // on purpose (ADR-005) and joining them here is the first step to joining them everywhere.
    platform
      .marketStandingsAsync(wanted.label, wanted.version)
      .then(setStandings)
      .catch(() => setStandings(null));
  }, [session.hasValidToken, signIns, platform, wanted.label, wanted.version]);

  // Hoisted above every early return, because a hook called inside the JSX of a component that
  // returns early is a hook called on some renders and not others - React refuses, and the
  // whole application renders nothing. The tests caught it as an empty page.
  const preview = useCallback(
    () =>
      version === null
        ? Promise.reject(new Error('There is no version to preview.'))
        : platform.previewAsync(version.documentIdentifier, version.version),
    [platform, version],
  );

  // The same hoisting rule as the preview above, and the same reason.
  const approvedTemplates = useCallback(
    () => platform.approvedTemplatesAsync(),
    [platform],
  );

  const filedRenders = useCallback(
    () =>
      version === null
        ? Promise.resolve([] as readonly FiledRender[])
        : platform.filedRendersAsync(version.documentIdentifier, version.version),
    [platform, version],
  );

  const produceRender = useCallback(
    (template: string) =>
      version === null
        ? Promise.resolve({
            ok: false as const,
            kind: 'failed' as const,
            detail: 'There is no version to render.',
          })
        : platform.produceRenderAsync(version.documentIdentifier, version.version, template),
    [platform, version],
  );

  const filedArtefact = useCallback(
    (template: string, templateVersion: number) =>
      version === null
        ? Promise.reject(new Error('There is no version to read an artefact for.'))
        : platform.filedRenderAsync(
            version.documentIdentifier, version.version, template, templateVersion),
    [platform, version],
  );

  const save = useCallback(
    async (sections: readonly SectionDescription[]) => {
      if (version === null) {
        return;
      }

      setOutcome(
        await platform.saveSections(
          version.documentIdentifier, version.version, sections, product));
    },
    [platform, version, product],
  );

  if (!session.hasValidToken) {
    return (
      <main>
        <h1>ePI authoring</h1>
        {problem !== null && <p role="alert">{problem}</p>}
        <p>
          You are signed out. Signing in happens at your organisation&apos;s identity provider -
          this application never asks for a password.
        </p>
        <button
          type="button"
          onClick={() => {
            // Where they were going, kept locally rather than handed to the identity provider.
            // Putting the label in the redirect URI would work and would also write it into
            // somebody else's logs; a label identifier is the platform's business.
            remember(address);
            void session.beginAsync().then(go);
          }}
        >
          Sign in
        </button>
      </main>
    );
  }

  if (wanted.label === null || wanted.version < 1) {
    return (
      <Shell>
        {address.searchParams.get('view') === 'tasks' ? (
          <WaitingWork
            tasks={() => platform.openTasks()}
            onOpen={open}
          />
        ) : (
          <LabelPicker
            search={(criteria) => platform.searchLabels(criteria)}
            onOpen={open}
          />
        )}
      </Shell>
    );
  }

  if (problem !== null) {
    return (
      <Shell>
        <p role="alert">{problem}</p>
      </Shell>
    );
  }

  if (version === null) {
    return (
      <Shell>
        <p role="status">Opening the label.</p>
      </Shell>
    );
  }

  return (
    <Shell>
      <Outcome outcome={outcome} />
      <LifecycleActions
        version={{
          state: version.state,
          documentIdentifier: version.documentIdentifier,
          version: version.version,
        }}
        actions={version.actions ?? []}
        signedActions={version.signedActions ?? []}
        signatureMeanings={version.signatureMeanings ?? {}}
        transition={(id, at, request) => platform.transitionAsync(id, at, request)}
        sign={(request) => platform.signAsync(request)}
        // Reopened, because a transition changes the state and what may be done from it - and a
        // screen still offering "submit" after submitting is a screen that will be wrong.
        onDone={() => setVersion(null)}
      />
      {standings !== null && (
        <MarketApprovals
          version={{
            documentIdentifier: version.documentIdentifier,
            version: version.version,
          }}
          markets={standings.marketActions}
          marketTransition={(id, at, market, request) =>
            platform.marketTransitionAsync(id, at, market, request)}
          sign={(request) => platform.signAsync(request)}
          onDone={() => setVersion(null)}
        />
      )}
      {/*
        One panel at a time, chosen by whether the version is approved. Showing both would put a
        preview and the artefact of record on the same screen, and CAP-RND-004 exists because
        somebody who cannot tell them apart eventually sends the wrong one.
      */}
      {version.state === 'approved' ? (
        <OfficialLeaflet
          approvedTemplates={approvedTemplates}
          filedRenders={filedRenders}
          produce={produceRender}
          artefact={filedArtefact}
        />
      ) : (
        <LeafletPreview load={preview} />
      )}
      <VersionHistory
        load={() => platform.versionRecordAsync(version.documentIdentifier, version.version)}
      />
      <ProductChoice
        current={product ?? version.product ?? null}
        searchProducts={(text) => platform.searchProducts(text)}
        onChoose={setProduct}
      />
      <LabelEditor
        version={version}
        onSave={(sections) => void save(sections)}
        alsoChanged={product !== undefined}
      />
    </Shell>
  );
}

/**
 * The places a signed-in author can go.
 *
 * @remarks
 * Two today - finding something to work on, and being told what somebody has asked of you - and
 * this exists now rather than at the fourth so that they do not arrive as a pile. The address is
 * the state: a link is a link, and somebody can send one.
 */
function Shell({ children }: { readonly children: React.ReactNode }) {
  return (
    <main>
      <h1>ePI authoring</h1>
      <nav>
        <a href="?">Find a label</a> <a href="?view=tasks">Waiting for you</a>
      </nav>
      {children}
    </main>
  );
}

/**
 * What the platform said about the last save.
 *
 * @remarks
 * The reason the client carries refusals rather than summarising them (ADR-038's endpoints, and
 * the client built for them): a refusal that reaches no screen is a refusal that did not happen
 * as far as the author is concerned. Each kind gets the sentence that says what to do about it,
 * because the remedies differ - and "unreachable" gets the one that says nothing was lost, since
 * an author who thinks their work went somewhere will close the tab.
 */
function Outcome({ outcome }: { readonly outcome: SaveOutcome | null }) {
  if (outcome === null) {
    return null;
  }

  if (outcome.ok) {
    return (
      <p role="status">
        Saved as version {outcome.version}. The version you were editing is unchanged.
      </p>
    );
  }

  switch (outcome.kind) {
    case 'refused':
      return (
        <div role="alert">
          <p>The platform refused this save. Its reasons, in its own words:</p>
          <ul>
            {outcome.problems.map((problem) => (
              <li key={problem}>{problem}</li>
            ))}
          </ul>
        </div>
      );
    case 'unauthenticated':
      return <p role="alert">Your sign-in has expired. Sign in again to save this work.</p>;
    case 'forbidden':
      return (
        <p role="alert">
          You are not allowed to write to this label. Ask whoever administers access for your
          affiliate and market. The platform said: {outcome.detail}
        </p>
      );
    case 'conflict':
      return (
        <p role="alert">
          Somebody else created the next version while you were editing. Reload and look at what
          they wrote before deciding what to do - saving again would write over it.
        </p>
      );
    case 'unreachable':
      return (
        <p role="alert">
          The platform could not be reached, so nothing was saved and nothing was lost. Your work
          is still here; try again.
        </p>
      );
  }
}

/** Where a remembered address is kept, beside the PKCE material the same round trip needs. */
const INTENDED = 'epi.intended';

/**
 * Notes where the author was going, before they are sent to the identity provider.
 *
 * @remarks
 * Locally rather than in the redirect URI. Putting the label there would work, and would also
 * write a label identifier into the identity provider's logs - which is the platform's business
 * and not its (ADR-051).
 */
function remember(address: URL): void {
  try {
    sessionStorage.setItem(INTENDED, address.href);
  } catch {
    // Storage can be refused. The author signs in either way and lands on the picker, which is
    // the behaviour this replaces rather than a new failure.
  }
}

/**
 * The address the author was heading for, if one was remembered, and otherwise the one they are
 * at (FN-AUT-019).
 *
 * @remarks
 * Consumed as it is read. Left behind, it would reopen weeks-old work on the next sign-in, and on
 * a shared machine it would reopen somebody else's label.
 *
 * Only on the way back from the identity provider. A plain visit to the root is somebody choosing
 * to start at the picker, and reopening whatever they last had would be overriding that.
 */
function intendedInsteadOf(location: URL): URL {
  if (!location.searchParams.has('code')) {
    return location;
  }

  let intended: string | null = null;

  try {
    intended = sessionStorage.getItem(INTENDED);
    sessionStorage.removeItem(INTENDED);
  } catch {
    return location;
  }

  if (intended === null || !URL.canParse(intended)) {
    return location;
  }

  // The callback's own parameters travel with it, so the exchange still has its code and state.
  const restored = new URL(intended);
  for (const [name, value] of location.searchParams) {
    restored.searchParams.set(name, value);
  }

  return restored;
}
