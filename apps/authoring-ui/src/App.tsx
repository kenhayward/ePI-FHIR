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
}: {
  readonly session: Session;
  readonly platform: Platform;
  readonly location: URL;
  readonly go: (url: string) => void;
}) {
  const [version, setVersion] = useState<VersionDescription | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<SaveOutcome | null>(null);
  const [standings, setStandings] = useState<MarketStandings | null>(null);

  // What the author picked, where they picked one. The address still wins when it names a
  // label, so a link opens what it says.
  const [picked, setPicked] = useState<{ label: string; version: number } | null>(null);

  // Undefined until the author changes it, and sent that way: omission is not removal, and the
  // platform reads an absent product as unchanged (ADR-040). Sending the current one back
  // unchanged would say nothing; sending null would detach the label from it.
  const [product, setProduct] = useState<ProductChoiceValue | undefined>(undefined);

  const fromAddress = location.searchParams.get('label');
  const wanted =
    fromAddress !== null
      ? { label: fromAddress, version: Number(location.searchParams.get('version') ?? '0') }
      : { label: picked?.label ?? null, version: picked?.version ?? 0 };

  const returning = location.searchParams.has('code');

  useEffect(() => {
    if (returning && !session.hasValidToken) {
      session.completeAsync(location).catch((failed: Error) => setProblem(failed.message));
    }
  }, [returning, session, location]);

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
  }, [session.hasValidToken, platform, wanted.label, wanted.version]);

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
        <button type="button" onClick={() => void session.beginAsync().then(go)}>
          Sign in
        </button>
      </main>
    );
  }

  if (wanted.label === null || wanted.version < 1) {
    return (
      <Shell>
        {location.searchParams.get('view') === 'tasks' ? (
          <WaitingWork
            tasks={() => platform.openTasks()}
            onOpen={(label, version) => setPicked({ label, version })}
          />
        ) : (
          <LabelPicker
            search={(criteria) => platform.searchLabels(criteria)}
            onOpen={(label, version) => setPicked({ label, version })}
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
