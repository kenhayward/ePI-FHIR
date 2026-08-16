import { useRef, useState } from 'react';
import { type Block, type Run, list, paragraph, text } from './authoring/narrative';
import { emphasise, referTo } from './authoring/runEditing';
import type { CrossReferenceTarget } from './authoring/editingSession';

/**
 * Writing one section, within what the write gate accepts (FN-AUT-013, FN-AUT-014).
 *
 * @remarks
 * <p>
 * Bounded by construction rather than by validation afterwards. There is no rich-text field,
 * because one would emit markup the write gate rejects after the author had finished writing
 * (ADR-037 decision 4) - so the controls are the model: paragraphs, lists, emphasis, and
 * references to other sections.
 * </p>
 * <p>
 * A paragraph is edited as its parts rather than as one string, and that is the decision worth
 * knowing. One field over a flattened paragraph means an edit to the words around a reference
 * can take the reference with it, silently, and an anchor lost that way is a cross-reference
 * that resolves to nothing (ADR-028). Each run gets its own field; a reference gets none,
 * because it is chosen and removed rather than retyped.
 * </p>
 */
export function SectionEditor({
  title,
  blocks,
  targets,
  onChange,
}: {
  readonly title: string;
  readonly blocks: readonly Block[];
  readonly targets: readonly CrossReferenceTarget[];
  readonly onChange: (blocks: readonly Block[]) => void;
}) {
  const fields = useRef(new Map<string, HTMLTextAreaElement>());
  const [referring, setReferring] = useState<{ block: number; run: number } | null>(null);
  const [target, setTarget] = useState('');
  const [problem, setProblem] = useState<string | null>(null);

  const replace = (at: number, block: Block) =>
    onChange(blocks.map((existing, index) => (index === at ? block : existing)));

  /** What the author has selected in one run's field, or nothing. */
  const selectionIn = (block: number, run: number) => {
    const field = fields.current.get(`${block}:${run}`);
    return field === undefined
      ? null
      : { start: field.selectionStart, end: field.selectionEnd };
  };

  const mark = (
    blockIndex: number,
    runIndex: number,
    apply: (runs: readonly Run[], start: number, end: number) => readonly Run[],
  ) => {
    const block = blocks[blockIndex];
    const selection = selectionIn(blockIndex, runIndex);

    if (block === undefined || block.kind !== 'paragraph' || selection === null) {
      return;
    }

    try {
      replace(blockIndex, paragraph(...apply(block.runs, selection.start, selection.end)));
      setProblem(null);
      setReferring(null);
      setTarget('');
    } catch (refused) {
      // Said rather than silently declined. A control that does nothing is one an author
      // decides is broken, and the commonest reason is having selected nothing.
      setProblem(refused instanceof Error ? refused.message : String(refused));
    }
  };

  return (
    <div>
      {problem !== null && <p role="alert">{problem}</p>}

      {blocks.map((block, blockIndex) =>
        block.kind === 'paragraph' ? (
          <div key={blockIndex}>
            <p>{`${title} paragraph ${blockIndex + 1}`}</p>

            {block.runs.map((run, runIndex) =>
              run.kind === 'crossReference' ? (
                // No field. A reference is chosen and removed, never retyped: an author editing
                // its words by hand would be editing an anchor by hand (ADR-028).
                <span key={runIndex}>
                  &quot;{run.value}&quot; refers to{' '}
                  {targets.find((candidate) => candidate.identity === run.target)?.title
                    ?? 'a section of this label'}{' '}
                  <button
                    type="button"
                    onClick={() =>
                      replace(
                        blockIndex,
                        paragraph(
                          ...block.runs.map((existing, index) =>
                            index === runIndex ? text(existing.value) : existing),
                        ),
                      )
                    }
                  >
                    Remove this reference
                  </button>
                </span>
              ) : (
                <div key={runIndex}>
                  <label>
                    <span>{`${title} paragraph ${blockIndex + 1} part ${runIndex + 1}`}</span>
                    <textarea
                      aria-label={`${title} paragraph ${blockIndex + 1} part ${runIndex + 1}`}
                      ref={(element) => {
                        if (element === null) {
                          fields.current.delete(`${blockIndex}:${runIndex}`);
                        } else {
                          fields.current.set(`${blockIndex}:${runIndex}`, element);
                        }
                      }}
                      value={run.value}
                      onChange={(event) =>
                        replace(
                          blockIndex,
                          paragraph(
                            ...block.runs.map((existing, index) =>
                              index === runIndex
                                ? { ...existing, value: event.target.value }
                                : existing),
                          ),
                        )
                      }
                    />
                  </label>

                  {run.kind === 'text' && (
                    <button
                      type="button"
                      onClick={() =>
                        mark(blockIndex, runIndex, (runs, start, end) =>
                          emphasise(runs, runIndex, start, end))
                      }
                    >
                      Emphasise the selected words
                    </button>
                  )}

                  {targets.length > 0 && run.kind === 'text' && (
                    <button
                      type="button"
                      onClick={() => setReferring({ block: blockIndex, run: runIndex })}
                    >
                      Refer to another section
                    </button>
                  )}

                  {referring?.block === blockIndex && referring.run === runIndex && (
                    <div>
                      <label>
                        Which section
                        <select value={target} onChange={(event) => setTarget(event.target.value)}>
                          <option value="">Choose a section</option>
                          {targets.map((candidate) => (
                            <option key={candidate.identity} value={candidate.identity}>
                              {candidate.title}
                            </option>
                          ))}
                        </select>
                      </label>
                      <button
                        type="button"
                        onClick={() =>
                          mark(blockIndex, runIndex, (runs, start, end) =>
                            referTo(runs, runIndex, start, end, target))
                        }
                      >
                        Insert
                      </button>
                      <button type="button" onClick={() => setReferring(null)}>
                        Cancel
                      </button>
                    </div>
                  )}
                </div>
              ),
            )}
          </div>
        ) : (
          <label key={blockIndex}>
            <span>{`${title} list ${blockIndex + 1}`}</span>
            <textarea
              aria-label={`${title} list ${blockIndex + 1}`}
              value={block.items.join('\n')}
              onChange={(event) => replace(blockIndex, list(event.target.value.split('\n')))}
            />
          </label>
        ),
      )}

      {/*
        The whole set of things that can be added. A control producing anything else would
        produce content the write gate rejects, which an author only discovers after writing it.
      */}
      <button type="button" onClick={() => onChange([...blocks, paragraph(text(''))])}>
        Add a paragraph
      </button>
      <button type="button" onClick={() => onChange([...blocks, list([''])])}>
        Add a list
      </button>
    </div>
  );
}
