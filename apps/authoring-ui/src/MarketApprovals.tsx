import { useState } from 'react';
import type { SignatureOutcome, TransitionOutcome } from './platform/client';

/** Where one market stands, and what the platform says may be done about it. */
export interface MarketStanding {
  readonly state: string;
  readonly actions: readonly string[];
  readonly signedActions: readonly string[];
  readonly actionsNeedingEffectiveDate: readonly string[];

  /** What a signature at each signed act must assert, as the platform configured it (ADR-020). */
  readonly signatureMeanings?: Readonly<Record<string, string>>;
}

/**
 * Where each market stands (FN-AUT-011).
 *
 * @remarks
 * Two rules the platform holds carefully, and this is the screen where they would blur.
 * Submitting to a regulator is an act of this organisation by an accountable person and is
 * signed; recording what a regulator decided is a factual entry about somebody else's decision
 * and is not (CAP-LCM-012). And only the transition that records an approval may say when it
 * takes effect (ADR-029 decision 3) - asking for that date anywhere else would be collecting one
 * the platform refuses.
 *
 * Both are the platform's answers, carried per market and never worked out here.
 */
export function MarketApprovals({
  version,
  markets,
  marketTransition,
  sign,
  onDone,
}: {
  readonly version: { readonly documentIdentifier: string; readonly version: number };
  readonly markets: Readonly<Record<string, MarketStanding>>;
  readonly marketTransition: (
    documentIdentifier: string,
    version: number,
    market: string,
    request: {
      action: string;
      reason?: string;
      signatureReference?: string;
      effectiveFrom?: string;
    },
  ) => Promise<TransitionOutcome>;
  readonly sign: (request: {
    documentIdentifier: string;
    version: number;
    meaning: string;
    password: string;
  }) => Promise<SignatureOutcome>;
  readonly onDone: () => void;
}) {
  const [acting, setActing] = useState<{ market: string; action: string } | null>(null);
  const [password, setPassword] = useState('');
  const [effectiveFrom, setEffectiveFrom] = useState('');
  const [problem, setProblem] = useState<string | null>(null);

  const clear = () => {
    setPassword('');
    setEffectiveFrom('');
    setActing(null);
  };

  const run = async (market: string, action: string) => {
    setProblem(null);
    const standing = markets[market]!;
    let signatureReference: string | undefined;

    if (standing.signedActions.includes(action)) {
      const signature = await sign({
        documentIdentifier: version.documentIdentifier,
        version: version.version,
        meaning: standing.signatureMeanings?.[action] ?? 'responsibility',
        password,
      });

      // Cleared before anything else, as everywhere a password is handled (ADR-041 decision 2).
      setPassword('');

      if (signature.refused) {
        setProblem(signature.detail);
        setActing(null);
        return;
      }

      signatureReference = signature.reference;
    }

    const outcome = await marketTransition(version.documentIdentifier, version.version, market, {
      action,
      ...(signatureReference === undefined ? {} : { signatureReference }),
      ...(standing.actionsNeedingEffectiveDate.includes(action) ? { effectiveFrom } : {}),
    });

    clear();

    if (!outcome.ok) {
      setProblem(outcome.detail);
      return;
    }

    onDone();
  };

  return (
    <section>
      <h2>Markets</h2>
      {problem !== null && <p role="alert">The platform refused that: {problem}</p>}

      <ul>
        {Object.entries(markets).map(([market, standing]) => (
          <li key={market}>
            <strong>{market}</strong>: {standing.state}{' '}
            {standing.actions.length === 0 ? (
              <span>- nothing to do here</span>
            ) : (
              standing.actions.map((action) => (
                <button key={action} type="button" onClick={() => setActing({ market, action })}>
                  {action}
                </button>
              ))
            )}

            {acting?.market === market && (
              <form
                onSubmit={(event) => {
                  event.preventDefault();
                  void run(market, acting.action);
                }}
              >
                {standing.signedActions.includes(acting.action) && (
                  <>
                    {/*
                      Dealing with a regulator on this organisation's behalf, so an accountable
                      person signs for it (CAP-LCM-012). Recording what a regulator decided is
                      not signed, and this is why the field is conditional rather than always.
                    */}
                    <p>You are signing to submit this version to a regulator.</p>
                    <label>
                      Password
                      <input
                        type="password"
                        autoComplete="off"
                        value={password}
                        onChange={(event) => setPassword(event.target.value)}
                      />
                    </label>
                  </>
                )}

                {standing.actionsNeedingEffectiveDate.includes(acting.action) && (
                  <label>
                    Takes effect
                    <input
                      type="date"
                      value={effectiveFrom}
                      onChange={(event) => setEffectiveFrom(event.target.value)}
                    />
                  </label>
                )}

                {/*
                  Named distinctly from the button that opened this. Two controls with the same
                  accessible name in one list item is "record-approval, record-approval" to
                  anybody listening to the page rather than looking at it.
                */}
                <button type="submit">Confirm {acting.action}</button>
                <button type="button" onClick={clear}>
                  Cancel
                </button>
              </form>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
