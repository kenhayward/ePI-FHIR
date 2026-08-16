import { useEffect, useState } from 'react';
import type { VersionRecord } from './platform/client';

/**
 * What happened to a version, who did it, and what they signed (FN-AUT-012).
 *
 * @remarks
 * All of this has been recorded since iteration 2 and none of it has ever been shown. An audit
 * trail nobody can read is one that exists for an inspection rather than for the people doing
 * the work.
 *
 * The one thing here that must never be subtle is a pinned package whose bytes no longer match.
 * That means what this version was validated against has changed underneath it - the failure the
 * whole pin exists to detect (ADR-023) - and it is the difference between a record that is
 * evidence and one that merely looks like evidence.
 */
export function VersionHistory({ load }: { readonly load: () => Promise<VersionRecord> }) {
  const [record, setRecord] = useState<VersionRecord | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    load()
      .then(setRecord)
      .catch((failed: Error) => setProblem(failed.message));
  }, [load]);

  if (problem !== null) {
    return (
      <section>
        <h2>History</h2>
        <p role="alert">This history could not be read, so it is not that there is none: {problem}</p>
      </section>
    );
  }

  if (record === null) {
    return (
      <section>
        <h2>History</h2>
        <p role="status">Reading the history.</p>
      </section>
    );
  }

  return (
    <section>
      <h2>History</h2>

      {record.pinnedContext === null ? (
        // No pin means nobody has approved this version. Saying "packages still match" would be
        // an assurance about something that does not exist.
        <p>This version has not been approved, so nothing was pinned against it.</p>
      ) : (
        <>
          {!record.packagesStillMatch && (
            <p role="alert">
              <strong>
                The conformance packages this version was approved against no longer match the
                bytes recorded then.
              </strong>{' '}
              What it was validated against has changed underneath it. Treat this version as
              unverified until somebody establishes why.
            </p>
          )}

          <h3>Approved against</h3>
          <ul>
            {record.pinnedContext.packages.map((pinned) => (
              <li key={`${pinned.name}@${pinned.version}`}>
                {pinned.name} {pinned.version}
              </li>
            ))}
            {/*
              Terminology beside the packages, because ADR-036 put it in the pinned context so
              this question could be answered - and a screen showing one without the other
              answers it incompletely.
            */}
            {record.pinnedContext.terminologyBindings.map((binding) => (
              <li key={binding.system}>
                {binding.system}{' '}
                {binding.isVersioned ? binding.version : '(the source could not say which version)'}
              </li>
            ))}
          </ul>
        </>
      )}

      <h3>What happened</h3>
      <ol>
        {record.history.map((entry) => (
          <li key={`${entry.at}-${entry.action}`}>
            <strong>{entry.action}</strong>: {entry.from} to {entry.to}, by {entry.actor} on{' '}
            {entry.at.slice(0, 10)}
            {entry.signature === null ? (
              // Stated rather than left blank. An unsigned transition is a fact about it, not a
              // gap - submitting is not a signed gate and should not read like one that lost
              // its signature.
              <div>Not a signed gate.</div>
            ) : (
              <div>
                Signed by {entry.signature.printedName} to mean {entry.signature.meaning}, over{' '}
                {entry.signature.contentHash}.
              </div>
            )}
          </li>
        ))}
      </ol>
    </section>
  );
}
