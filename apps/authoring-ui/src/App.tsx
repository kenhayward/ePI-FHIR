import { useCallback, useEffect, useState } from 'react';
import { LabelEditor } from './LabelEditor';
import type { SectionDescription, VersionDescription } from './authoring/editingSession';
import type { SaveOutcome } from './platform/client';

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
  ): Promise<SaveOutcome>;
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

  const wanted = {
    label: location.searchParams.get('label'),
    version: Number(location.searchParams.get('version') ?? '0'),
  };

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
  }, [session.hasValidToken, platform, wanted.label, wanted.version]);

  const save = useCallback(
    async (sections: readonly SectionDescription[]) => {
      if (version === null) {
        return;
      }

      setOutcome(await platform.saveSections(version.documentIdentifier, version.version, sections));
    },
    [platform, version],
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
      <main>
        <h1>ePI authoring</h1>
        <p>
          This address does not say which label to open. Follow a link to a label version, or add
          a label and version to the address.
        </p>
      </main>
    );
  }

  if (problem !== null) {
    return (
      <main>
        <h1>ePI authoring</h1>
        <p role="alert">{problem}</p>
      </main>
    );
  }

  if (version === null) {
    return (
      <main>
        <h1>ePI authoring</h1>
        <p role="status">Opening the label.</p>
      </main>
    );
  }

  return (
    <main>
      <Outcome outcome={outcome} />
      <LabelEditor version={version} onSave={(sections) => void save(sections)} />
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
