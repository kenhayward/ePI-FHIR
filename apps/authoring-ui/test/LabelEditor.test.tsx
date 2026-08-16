import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { LabelEditor } from '../src/LabelEditor';
import { paragraph, serialiseNarrative, text } from '../src/authoring/narrative';

// The surface an author sees (FN-AUT-003).
//   CAP-TPL-005 Template-driven guided authoring that shields authors from raw FHIR
//
// ADR-037 decision 2: an author edits sections, and never sees a Bundle, a resource type or a
// canonical URL. These cases are about what is on the screen, because that is the whole of what
// this layer decides - every rule it reflects is enforced on the server.
describe('FN-AUT-003 the label editor', () => {
  const narrative = (words: string) => serialiseNarrative([paragraph(text(words))]);

  const version = {
    documentIdentifier: '01a00000-0000-7000-8000-00000000000a',
    version: 2,
    state: 'draft',
    editable: true,
    sections: [
      { identity: 'sec-1', title: '1. What Examplinum is', narrative: narrative('A medicine.') },
      { identity: 'sec-2', title: '2. Before you take it', narrative: narrative('Read this.') },
    ],
  };

  it('shows the author their sections by title', () => {
    render(<LabelEditor version={version} onSave={vi.fn()} />);

    expect(screen.getByRole('heading', { name: '1. What Examplinum is' })).toBeDefined();
    expect(screen.getByRole('heading', { name: '2. Before you take it' })).toBeDefined();
  });

  it('shows no FHIR anywhere', () => {
    // The one that fails the moment somebody puts a document identifier in a corner "just for
    // debugging". An author who has to know what a Bundle is has not been shielded from
    // anything (ADR-037 decision 2).
    const { container } = render(<LabelEditor version={version} onSave={vi.fn()} />);

    const shown = container.textContent ?? '';
    for (const leak of ['Bundle', 'Composition', 'http://', 'urn:uuid', version.documentIdentifier]) {
      expect(shown).not.toContain(leak);
    }
  });

  it('lets an author change a section and offers to save it', async () => {
    const save = vi.fn();
    render(<LabelEditor version={version} onSave={save} />);

    await userEvent.clear(screen.getByLabelText('1. What Examplinum is'));
    await userEvent.type(screen.getByLabelText('1. What Examplinum is'), 'A medicine for adults.');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    expect(save).toHaveBeenCalledWith([
      {
        identity: 'sec-1',
        title: '1. What Examplinum is',
        narrative: narrative('A medicine for adults.'),
      },
      { identity: 'sec-2', title: '2. Before you take it', narrative: narrative('Read this.') },
    ]);
  });

  it('will not save a version with nothing changed', async () => {
    // Saving mints a version, and one identical to its predecessor is a version somebody has to
    // explain.
    const save = vi.fn();
    render(<LabelEditor version={version} onSave={save} />);

    expect(screen.getByRole('button', { name: /save/i })).toHaveProperty('disabled', true);
    expect(save).not.toHaveBeenCalled();
  });

  it('offers no editor at all for an approved version, and says why', async () => {
    render(
      <LabelEditor version={{ ...version, state: 'approved', editable: false }} onSave={vi.fn()} />,
    );

    expect(screen.queryByLabelText('1. What Examplinum is')).toBeNull();
    expect(screen.getAllByText(/approved and cannot be changed/i).length).toBeGreaterThan(0);
  });

  it('says why a section it cannot represent is read-only, rather than hiding it', async () => {
    // A section the author cannot see is a section they will assume is missing.
    render(
      <LabelEditor
        version={{
          ...version,
          sections: [
            {
              identity: 'sec-3',
              title: '3. Dosage',
              narrative: '<div xmlns="http://www.w3.org/1999/xhtml"><table/></div>',
            },
          ],
        }}
        onSave={vi.fn()}
      />,
    );

    expect(screen.getByRole('heading', { name: '3. Dosage' })).toBeDefined();
    expect(screen.getByText(/cannot represent/i)).toBeDefined();
  });
});
