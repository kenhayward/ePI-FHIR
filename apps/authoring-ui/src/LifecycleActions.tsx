import { useState } from 'react';
import type { SignatureOutcome, TransitionOutcome, TransitionRequest } from './platform/client';

/**
 * Moving a version through its lifecycle, and signing where the gate says so (FN-AUT-010).
 *
 * @remarks
 * The platform decides what may happen and refuses what may not (ADR-037 decision 1). Nothing
 * here is a control: this offers the actions it was told about, and every one of them is checked
 * again on the server. What it is responsible for is that a refusal reaches the person in the
 * platform's own words, because those refusals are the interesting part - "the author of a
 * version may not approve it" is far more use than "forbidden".
 */
export function LifecycleActions({
  version,
  actions,
  signedActions = [],
  signatureMeanings = {},
  transition,
  sign,
  onDone,
}: {
  readonly version: {
    readonly state: string;
    readonly documentIdentifier: string;
    readonly version: number;
  };
  readonly actions: readonly string[];
  readonly signedActions?: readonly string[];

  /**
   * What a signature at each signed gate must assert, as the platform configured it.
   *
   * @remarks
   * Not chosen here. A signature that says the wrong thing is worse than none - the gate refuses
   * it, and the record would have asserted something nobody intended (ADR-020). This used to be
   * a literal, which happened to match and would have stopped matching the moment a deployment
   * configured a different meaning.
   */
  readonly signatureMeanings?: Readonly<Record<string, string>>;
  readonly transition: (
    documentIdentifier: string,
    version: number,
    request: TransitionRequest,
  ) => Promise<TransitionOutcome>;
  readonly sign: (request: {
    documentIdentifier: string;
    version: number;
    meaning: string;
    password: string;
  }) => Promise<SignatureOutcome>;
  readonly onDone: () => void;
}) {
  const [signing, setSigning] = useState<string | null>(null);
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const [moved, setMoved] = useState<string | null>(null);

  const run = async (action: string, signatureReference?: string) => {
    setProblem(null);
    const outcome = await transition(version.documentIdentifier, version.version, {
      action,
      ...(signatureReference === undefined ? {} : { signatureReference }),
    });

    if (!outcome.ok) {
      setProblem(outcome.detail);
      return;
    }

    setMoved(outcome.to);
    onDone();
  };

  const signThenRun = async (action: string) => {
    const signature = await sign({
      documentIdentifier: version.documentIdentifier,
      version: version.version,
      meaning: signatureMeanings[action] ?? 'approval',
      password,
    });

    // Cleared the moment it has been used, and before anything else happens. It exists for one
    // request (ADR-041 decision 2); anything holding it afterwards is a credential sitting in a
    // browser for as long as the tab is open.
    setPassword('');
    setSigning(null);

    if (signature.refused) {
      // The transition is not attempted. A wrong password must not become a transition that
      // fails for a reason nobody can act on.
      setProblem(signature.detail);
      return;
    }

    await run(action, signature.reference);
  };

  return (
    <section>
      <h2>This version is {version.state}</h2>

      {problem !== null && <p role="alert">The platform refused that: {problem}</p>}
      {moved !== null && <p role="status">Moved to {moved}.</p>}

      {signing === null ? (
        actions.map((action) => (
          <button
            key={action}
            type="button"
            onClick={() =>
              signedActions.includes(action) ? setSigning(action) : void run(action)
            }
          >
            {action}
          </button>
        ))
      ) : (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void signThenRun(signing);
          }}
        >
          {/*
            A password field, on one screen, for one purpose - and this comment is here because
            this is the kind of thing that gets copied. It is not a sign-in: signing in is
            delegated to the identity provider and this application never sees a credential for
            it (ADR-039 decision 1). Re-entering it here is the control that makes a signature
            attributable to a person rather than to a session somebody left open (ADR-041).
          */}
          <p>
            You are signing this record, not signing in. Your credentials are checked at the
            moment of signing so that the signature is attributable to you.
          </p>
          <label>
            Password
            <input
              type="password"
              autoComplete="off"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </label>
          <button type="submit">Sign and {signing}</button>
          <button
            type="button"
            onClick={() => {
              setPassword('');
              setSigning(null);
            }}
          >
            Cancel
          </button>
        </form>
      )}
    </section>
  );
}
