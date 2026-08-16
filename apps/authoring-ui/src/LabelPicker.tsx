import { useState } from 'react';
import type { SearchCriteria, SearchResults } from './platform/client';

/**
 * Finding a label to open (FN-AUT-007).
 *
 * @remarks
 * The platform bounds a search by what the caller is permitted to see rather than filtering its
 * results (ADR-022 decision 1), which makes an empty answer genuinely ambiguous: there may be no
 * such label, or there may be one this person is not allowed to know about. The platform
 * deliberately will not say which, and neither does this - so the wording is "nothing you are
 * allowed to see" rather than "no labels found", which would resolve that ambiguity in the one
 * direction the design refuses to.
 */
export function LabelPicker({
  search,
  onOpen,
}: {
  readonly search: (criteria: SearchCriteria) => Promise<SearchResults>;
  readonly onOpen: (documentIdentifier: string, version: number) => void;
}) {
  const [text, setText] = useState('');
  const [results, setResults] = useState<SearchResults | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const run = async () => {
    setProblem(null);

    try {
      // Only what was given. An empty filter sent as an empty string is a filter, and it would
      // present as "there are no labels" rather than as a mistake.
      setResults(await search(text.trim() === '' ? {} : { text: text.trim() }));
    } catch (failed) {
      // Never shown as an empty result: a failure presented that way sends the author looking
      // for a label that is there.
      setResults(null);
      setProblem(failed instanceof Error ? failed.message : String(failed));
    }
  };

  return (
    <section>
      <h2>Find a label</h2>

      <label>
        Search for a label
        <input
          type="search"
          value={text}
          onChange={(event) => setText(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              void run();
            }
          }}
        />
      </label>
      <button type="button" onClick={() => void run()}>
        Search
      </button>

      {problem !== null && (
        <p role="alert">
          That search did not happen, so this is not an empty result: {problem}
        </p>
      )}

      {results !== null && results.hits.length === 0 && (
        <p role="status">
          Nothing you are allowed to see matches that. There may be no such label, or there may
          be one outside your affiliate and markets - the platform does not distinguish the two,
          deliberately.
        </p>
      )}

      {results !== null && results.hits.length > 0 && (
        <>
          <p role="status">
            Showing {results.hits.length} of {results.total}.
          </p>
          <ul>
            {results.hits.map((label) => (
              <li key={`${label.documentIdentifier}@${label.version}`}>
                <button
                  type="button"
                  onClick={() => onOpen(label.documentIdentifier, label.version)}
                >
                  {label.title}
                </button>{' '}
                {/*
                  A title alone is not enough to choose by: the same label exists in several
                  markets and several versions, and opening the wrong one is a wasted edit at
                  best.
                */}
                <span>
                  version {label.version}, {label.market}, {label.state}
                </span>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}
