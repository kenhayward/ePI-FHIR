import { describe, expect, it } from 'vitest';
import { emphasise, referTo, plainOnly } from '../src/authoring/runEditing';
import { crossReference, emphasis, text } from '../src/authoring/narrative';

// Marking part of what somebody wrote (FN-AUT-014).
//   CAP-TPL-005 Template-driven guided authoring that shields authors from raw FHIR
//   CAP-SCM-005 Cross-references within a document
//
// Emphasis and a mid-sentence reference are the same problem: a range of characters inside one
// run becomes a run of its own. Kept here, as functions over the model, because it is the part
// that can be got quietly wrong - an off-by-one loses a character out of a regulated sentence -
// and because a DOM has nothing to do with it.
describe('FN-AUT-014 marking part of a sentence', () => {
  const sentence = [text('Do not exceed two tablets.')];

  it('emphasises exactly the characters chosen, and nothing either side', () => {
    const marked = emphasise(sentence, 0, 7, 14);

    expect(marked).toEqual([text('Do not '), emphasis('exceed '), text('two tablets.')]);
  });

  it('leaves nothing empty when the selection starts at the beginning', () => {
    // An empty text run either side would serialise to nothing and read back as nothing, so it
    // is not wrong exactly - it is noise in a model whose round trip is asserted elsewhere.
    expect(emphasise(sentence, 0, 0, 6)).toEqual([emphasis('Do not'), text(' exceed two tablets.')]);
  });

  it('leaves nothing empty when the selection runs to the end', () => {
    expect(emphasise([text('Take one')], 0, 5, 8)).toEqual([text('Take '), emphasis('one')]);
  });

  it('refuses a selection of nothing', () => {
    // Emphasising an insertion point would add an empty run that shows as nothing and can never
    // be selected again to remove.
    expect(() => emphasise(sentence, 0, 5, 5)).toThrow(/nothing/i);
  });

  it('turns the chosen words into a reference to the section the author picked', () => {
    const referred = referTo([text('See section 4.2 for warnings.')], 0, 4, 15, 'sec-4-2');

    expect(referred).toEqual([
      text('See '),
      crossReference('sec-4-2', 'section 4.2'),
      text(' for warnings.'),
    ]);
  });

  it('keeps the words the author chose as what the reader sees', () => {
    // The label is the author's words, not the section's title. A reference reading "3. How to
    // take it" in the middle of a sentence is a reference nobody would have written.
    const referred = referTo([text('as described earlier')], 0, 3, 20, 'sec-2');

    expect(referred[1]).toEqual(crossReference('sec-2', 'described earlier'));
  });

  it('marks part of one run and leaves the others alone', () => {
    const runs = [text('Take '), emphasis('one'), text(' tablet daily.')];

    expect(emphasise(runs, 2, 1, 7)).toEqual([
      text('Take '),
      emphasis('one'),
      text(' '),
      emphasis('tablet'),
      text(' daily.'),
    ]);
  });

  it('refuses to mark a run that is already a reference', () => {
    // A reference inside a reference is not expressible, and an emphasis inside one would
    // silently drop on serialising - the model has no nesting and it should say so.
    const runs = [text('See '), crossReference('sec-2', 'section 2')];

    expect(() => emphasise(runs, 1, 0, 3)).toThrow(/reference/i);
  });

  it('refuses a run that is not there', () => {
    expect(() => emphasise(sentence, 4, 0, 2)).toThrow(/no run/i);
  });

  it('reports which runs an author may edit as text', () => {
    // A reference is chosen and removed, never retyped (ADR-028), so it is not among them.
    const runs = [text('See '), crossReference('sec-2', 'section 2'), emphasis('carefully')];

    expect(plainOnly(runs).map((entry) => entry.index)).toEqual([0, 2]);
  });
});
