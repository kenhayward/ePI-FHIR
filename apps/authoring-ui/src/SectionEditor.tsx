import { useState } from 'react';
import { type Block, type Run, crossReference, list, paragraph, text } from './authoring/narrative';
import type { CrossReferenceTarget } from './authoring/editingSession';

/**
 * Writing one section, within what the write gate accepts (FN-AUT-013).
 *
 * @remarks
 * <p>
 * Bounded by construction rather than by validation afterwards. There is no rich-text field
 * here, because one would emit markup the write gate rejects after the author had finished
 * writing (ADR-037 decision 4) - so the controls are the model: paragraphs, lists, and
 * references to other sections.
 * </p>
 * <p>
 * The reference control is what pays ADR-028's debt. The author picks the section they mean by
 * its title and this writes the identifier; nobody types one, and none is ever shown.
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
  const [referring, setReferring] = useState<number | null>(null);
  const [target, setTarget] = useState('');
  const [label, setLabel] = useState('');

  const replace = (at: number, block: Block) =>
    onChange(blocks.map((existing, index) => (index === at ? block : existing)));

  const insertReference = (at: number) => {
    const into = blocks[at];
    if (into === undefined || into.kind !== 'paragraph' || target === '') {
      return;
    }

    replace(at, paragraph(...into.runs, text(' '), crossReference(target, label)));
    setReferring(null);
    setTarget('');
    setLabel('');
  };

  return (
    <div>
      {blocks.map((block, index) =>
        block.kind === 'paragraph' ? (
          <div key={index}>
            <label>
              <span>{`${title} paragraph ${index + 1}`}</span>
              <textarea
                aria-label={`${title} paragraph ${index + 1}`}
                value={block.runs
                  .filter((run) => run.kind !== 'crossReference')
                  .map((run) => run.value)
                  .join('')}
                onChange={(event) =>
                  replace(index, paragraph(text(event.target.value), ...references(block.runs)))
                }
              />
            </label>

            {/*
              A reference is shown and not editable as text. An author retyping the words of one
              would be editing an anchor by hand, which is exactly what ADR-028's debt is about.
              Changing which section it points at is a separate act.
            */}
            {references(block.runs).map((reference) => (
              <p key={reference.target}>
                &quot;{reference.value}&quot; refers to{' '}
                {targets.find((candidate) => candidate.identity === reference.target)?.title
                  ?? 'a section of this label'}
              </p>
            ))}

            {targets.length > 0 && referring !== index && (
              <button type="button" onClick={() => setReferring(index)}>
                Refer to another section
              </button>
            )}

            {referring === index && (
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
                <label>
                  What the reader sees
                  <input
                    type="text"
                    value={label}
                    onChange={(event) => setLabel(event.target.value)}
                  />
                </label>
                <button type="button" onClick={() => insertReference(index)}>
                  Insert
                </button>
                <button type="button" onClick={() => setReferring(null)}>
                  Cancel
                </button>
              </div>
            )}
          </div>
        ) : (
          <label key={index}>
            <span>{`${title} list ${index + 1}`}</span>
            <textarea
              aria-label={`${title} list ${index + 1}`}
              value={block.items.join('\n')}
              onChange={(event) => replace(index, list(event.target.value.split('\n')))}
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

const references = (runs: readonly Run[]) =>
  runs.filter((run): run is Extract<Run, { kind: 'crossReference' }> =>
    run.kind === 'crossReference');
