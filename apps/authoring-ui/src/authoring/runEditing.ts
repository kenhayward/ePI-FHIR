import { type Run, crossReference, emphasis, text } from './narrative';

/**
 * Marking part of what somebody wrote (FN-AUT-014).
 *
 * @remarks
 * Emphasis and a mid-sentence reference are the same problem: a range of characters inside one
 * run becomes a run of its own. It lives here, as functions over the model, because it is the
 * part that can be got quietly wrong - an off-by-one loses a character out of a regulated
 * sentence - and because a DOM has nothing to do with it.
 *
 * Every operation names the run it applies to rather than an offset into the whole paragraph.
 * A paragraph's runs are not one string, and pretending otherwise is how an edit lands inside a
 * reference and takes its anchor with it.
 */

/** Splits one run into what precedes the selection, the selection, and what follows. */
const split = (runs: readonly Run[], index: number, start: number, end: number) => {
  const run = runs[index];

  if (run === undefined) {
    throw new Error(`There is no run ${index} in this paragraph.`);
  }

  if (run.kind === 'crossReference') {
    throw new Error(
      'That part of the sentence is a reference to another section. The model has no nesting, '
        + 'so marking inside one would drop on saving - remove the reference first.',
    );
  }

  if (start >= end) {
    throw new Error(
      'Nothing is selected. Marking an insertion point would add a run that shows as nothing '
        + 'and can never be selected again to remove.',
    );
  }

  return {
    before: run.value.slice(0, start),
    chosen: run.value.slice(start, end),
    after: run.value.slice(end),
  };
};

/** Replaces one run with up to three, dropping the empty ones. */
const rebuild = (
  runs: readonly Run[],
  index: number,
  before: string,
  middle: Run,
  after: string,
): readonly Run[] => [
  ...runs.slice(0, index),

  // Empty runs either side are dropped: they serialise to nothing and read back as nothing, so
  // they are noise in a model whose round trip is asserted.
  ...(before === '' ? [] : [text(before)]),
  middle,
  ...(after === '' ? [] : [text(after)]),
  ...runs.slice(index + 1),
];

/** Emphasises the chosen characters of one run. */
export const emphasise = (
  runs: readonly Run[],
  index: number,
  start: number,
  end: number,
): readonly Run[] => {
  const { before, chosen, after } = split(runs, index, start, end);
  return rebuild(runs, index, before, emphasis(chosen), after);
};

/**
 * Turns the chosen characters of one run into a reference to another section.
 *
 * @remarks
 * The chosen words become what a reader sees, not the target section's title: a reference
 * reading "3. How to take it" in the middle of a sentence is one nobody would have written. The
 * identifier is what the author picked from a list of titles and never typed (ADR-028).
 */
export const referTo = (
  runs: readonly Run[],
  index: number,
  start: number,
  end: number,
  target: string,
): readonly Run[] => {
  const { before, chosen, after } = split(runs, index, start, end);
  return rebuild(runs, index, before, crossReference(target, chosen), after);
};

/**
 * The runs an author may edit as text, with where each sits.
 *
 * @remarks
 * A reference is chosen and removed, never retyped (ADR-028), so it is not among them.
 */
export const plainOnly = (runs: readonly Run[]): readonly { index: number; run: Run }[] =>
  runs
    .map((run, index) => ({ index, run }))
    .filter((entry) => entry.run.kind !== 'crossReference');
