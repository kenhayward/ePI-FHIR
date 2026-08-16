import { useState } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { SectionEditor } from '../src/SectionEditor';
import { crossReference, emphasis, list, paragraph, text } from '../src/authoring/narrative';

// Writing a section, within what the write gate accepts (FN-AUT-013).
//   CAP-TPL-005 Template-driven guided authoring that shields authors from raw FHIR
//   CAP-SCM-005 Cross-references within a document, with referential integrity
//
// The narrative model has carried paragraphs, lists and cross-references since the first slice,
// and the control was a text area that could only produce paragraphs. So ADR-028's debt was
// half paid: the model could express an anchor and an author could not insert one.
//
// Everything here is bounded by construction. There is no rich-text field, because one would
// emit markup the write gate rejects after the author had finished writing (ADR-037 decision 4).
describe('FN-AUT-013 writing a section', () => {
  const targets = [
    { identity: 'sec-2', title: '2. Before you take it' },
    { identity: 'sec-3', title: '3. How to take it' },
  ];

  /**
   * Rendered the way the application renders it: controlled, with what it reports fed back.
   *
   * A spy alone would leave the component holding its original value however much was typed,
   * because a controlled field shows what it was given rather than what was keyed - so the test
   * would be measuring the harness rather than the editor.
   */
  const shown = (
    initial: Parameters<typeof SectionEditor>[0]['blocks'] = [paragraph(text('A medicine.'))],
  ) => {
    const onChange = vi.fn();

    function Harness() {
      const [blocks, setBlocks] = useState(initial);

      return (
        <SectionEditor
          title="1. What Examplinum is"
          blocks={blocks}
          targets={targets}
          onChange={(next) => {
            onChange(next);
            setBlocks(next);
          }}
        />
      );
    }

    render(<Harness />);
    return onChange;
  };

  it('shows each paragraph as its own field', async () => {
    shown([paragraph(text('First.')), paragraph(text('Second.'))]);

    expect(screen.getByDisplayValue('First.')).toBeDefined();
    expect(screen.getByDisplayValue('Second.')).toBeDefined();
  });

  it('reports a paragraph the author rewrote', async () => {
    const onChange = shown();

    await userEvent.clear(screen.getByDisplayValue('A medicine.'));
    await userEvent.type(screen.getByRole('textbox', { name: /paragraph 1/i }), 'Rewritten.');

    expect(onChange).toHaveBeenLastCalledWith([paragraph(text('Rewritten.'))]);
  });

  it('adds a paragraph, and a list, and nothing else', async () => {
    // The whole set. A control that could produce anything else would produce content the write
    // gate rejects, which an author only finds out after writing it.
    const onChange = shown();

    await userEvent.click(screen.getByRole('button', { name: /add a paragraph/i }));
    expect(onChange).toHaveBeenLastCalledWith([paragraph(text('A medicine.')), paragraph(text(''))]);

    await userEvent.click(screen.getByRole('button', { name: /add a list/i }));
    expect(onChange).toHaveBeenLastCalledWith([
      paragraph(text('A medicine.')),
      paragraph(text('')),
      list(['']),
    ]);
  });

  it('writes a list as one item per line', async () => {
    shown([list(['With food.', 'With water.'])]);

    // By role rather than display value: a textarea's value carries the newline, and matching on
    // it is matching on how the lines happen to be joined rather than on what is shown.
    expect(screen.getByRole('textbox', { name: /list 1/i })).toHaveProperty(
      'value',
      'With food.\nWith water.',
    );
  });

  it('offers the sections of this label as cross-reference targets', async () => {
    // ADR-037 decision 3 and ADR-028: the author picks the section they mean, by its title, and
    // the surface writes the identifier. Nobody types a section identifier.
    shown();

    await userEvent.click(screen.getByRole('button', { name: /refer to another section/i }));

    expect(await screen.findByRole('option', { name: '2. Before you take it' })).toBeDefined();
  });

  // The reference used to be appended to the end of the paragraph with its label typed
  // separately, because there was no way to say which words it applied to. It is now made from
  // the selection - see the mid-sentence case below, which is what that could not do.

  it('never shows a section identifier anywhere', async () => {
    // The identity is what the platform resolves and it means nothing to the person choosing.
    const { container } = render(
      <SectionEditor
        title="1. What Examplinum is"
        blocks={[paragraph(text('See '), crossReference('sec-2', 'section 2'))]}
        targets={targets}
        onChange={vi.fn()}
      />,
    );

    expect(container.textContent).not.toContain('sec-2');
  });

  it('says which words in a paragraph are a reference, without letting them be retyped', async () => {
    // An author editing the text of a reference by hand would be editing an anchor by hand,
    // which is the thing ADR-028's debt is about. The words are shown; changing them is a
    // separate act.
    shown([paragraph(text('See '), crossReference('sec-2', 'section 2'), text(' for warnings.'))]);

    expect(screen.getByText(/refers to 2\. Before you take it/i)).toBeDefined();
  });

  it('gives each part of a paragraph its own field, so editing one cannot disturb another', async () => {
    // The reason this is segmented rather than one field over a flat string: an edit to the
    // words around a reference must not be able to take the reference with it.
    shown([paragraph(text('See '), crossReference('sec-2', 'section 2'), text(' for warnings.'))]);

    // By label rather than display value: Testing Library normalises whitespace when matching a
    // value, so a run ending in a space cannot be told from one that does not.
    expect(screen.getByRole('textbox', { name: /paragraph 1 part 1/ })).toHaveProperty(
      'value', 'See ');
    expect(screen.getByRole('textbox', { name: /paragraph 1 part 3/ })).toHaveProperty(
      'value', ' for warnings.');
  });

  it('emphasises what the author selected, and nothing either side', async () => {
    const onChange = shown([paragraph(text('Do not exceed two tablets.'))]);
    const field = screen.getByDisplayValue('Do not exceed two tablets.') as HTMLTextAreaElement;

    field.setSelectionRange(7, 13);
    await userEvent.click(screen.getAllByRole('button', { name: /emphasise/i })[0]!);

    expect(onChange).toHaveBeenLastCalledWith([
      paragraph(text('Do not '), emphasis('exceed'), text(' two tablets.')),
    ]);
  });

  it('makes the selected words the reference, in the middle of a sentence', async () => {
    // What could not be done before: a reference landed at the end of the paragraph, so
    // "see section 4.2 for warnings" put it after "warnings".
    const onChange = shown([paragraph(text('See section 4.2 for warnings.'))]);
    const field = screen.getByDisplayValue('See section 4.2 for warnings.') as HTMLTextAreaElement;

    field.setSelectionRange(4, 15);
    await userEvent.click(screen.getAllByRole('button', { name: /refer to another section/i })[0]!);
    await userEvent.selectOptions(screen.getByLabelText(/which section/i), 'sec-3');
    await userEvent.click(screen.getByRole('button', { name: /^insert$/i }));

    expect(onChange).toHaveBeenLastCalledWith([
      paragraph(text('See '), crossReference('sec-3', 'section 4.2'), text(' for warnings.')),
    ]);
  });

  it('says so rather than doing nothing when nothing is selected', async () => {
    // A control that silently declines is a control an author decides is broken.
    shown([paragraph(text('Do not exceed.'))]);

    await userEvent.click(screen.getAllByRole('button', { name: /emphasise/i })[0]!);

    expect(await screen.findByRole('alert')).toBeDefined();
  });

  it('lets a reference be removed, which is the only way to change its words', async () => {
    const onChange = shown([paragraph(text('See '), crossReference('sec-2', 'section 2'))]);

    await userEvent.click(screen.getByRole('button', { name: /remove this reference/i }));

    expect(onChange).toHaveBeenLastCalledWith([paragraph(text('See '), text('section 2'))]);
  });

  it('offers no reference to insert when there is nowhere to refer to', async () => {
    // A label of one section has nothing to cross-refer to, and a control that opened onto an
    // empty list is a control that looks broken.
    render(
      <SectionEditor
        title="1. Only section"
        blocks={[paragraph(text('Alone.'))]}
        targets={[]}
        onChange={vi.fn()}
      />,
    );

    expect(screen.queryByRole('button', { name: /refer to another section/i })).toBeNull();
  });
});
