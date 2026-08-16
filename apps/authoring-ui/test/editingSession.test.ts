import { describe, expect, it } from 'vitest';
import { openSession } from '../src/authoring/editingSession';
import { paragraph, serialiseNarrative, text } from '../src/authoring/narrative';

// The working copy an author edits (FN-AUT-002).
//   CAP-TPL-005 Template-driven guided authoring that shields authors from raw FHIR
//   CAP-LCM-002 Version every label as immutable snapshots
//
// ADR-037 decisions 1, 5 and 6. Every rule here is one the platform already enforces; what the
// session does is reflect it, so an author is not offered an action that will certainly fail.
// Where the two disagree, the platform is right and this has a defect.
describe('FN-AUT-002 the working copy', () => {
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

  it('opens every section the platform sent', () => {
    const session = openSession(version);

    expect(session.sections.map((s) => s.identity)).toEqual(['sec-1', 'sec-2']);
    expect(session.sections.every((s) => s.editable)).toBe(true);
  });

  it('has nothing to save until something is edited', () => {
    // Saving mints a version (ADR-037 decision 6), and a version identical to the one before it
    // is a version somebody has to explain.
    expect(openSession(version).hasUnsavedWork).toBe(false);
  });

  it('knows which sections were changed and which were not', () => {
    const session = openSession(version);

    session.edit('sec-1', [paragraph(text('A medicine for adults.'))]);

    expect(session.hasUnsavedWork).toBe(true);
    expect(session.changed.map((s) => s.identity)).toEqual(['sec-1']);
  });

  it('shows what has been typed, not what was opened', () => {
    // This was a defect: editing updated the working copy while the section list kept the text
    // parsed when the session opened, so anything reading it showed the original however much
    // had been typed. Two copies of one thing, and the one on the screen was not the one that
    // would be saved. Found by the editor's own test, and asserted here because it is the
    // session's to get right.
    const session = openSession(version);

    session.edit('sec-1', [paragraph(text('A medicine for adults.'))]);

    expect(session.sections[0]!.blocks).toEqual([paragraph(text('A medicine for adults.'))]);
  });

  it('stops counting a section as changed once it is put back as it was', () => {
    // An author who types a word and deletes it has not changed the label.
    const session = openSession(version);

    session.edit('sec-1', [paragraph(text('Changed.'))]);
    session.edit('sec-1', [paragraph(text('A medicine.'))]);

    expect(session.hasUnsavedWork).toBe(false);
  });

  it('offers no editor where the platform says this caller may not write', () => {
    // ADR-037 decision 5 said this was about the version's state, and ADR-038 decision 6
    // corrects that: every version is immutable, saving mints the next one, and drafting from
    // an approved version is how a label evolves. What decides this is whether the caller may
    // write to the document at all - the platform's answer, carried in `editable`, never
    // re-derived here.
    const session = openSession({ ...version, editable: false });

    expect(session.sections.every((s) => s.editable)).toBe(false);
    expect(() => session.edit('sec-1', [paragraph(text('...'))])).toThrow(/not allowed/i);
  });

  it('opens an approved version, because saving it drafts the next one', () => {
    // The case that would have caught the mistake. A surface refusing this would be disabling
    // something the platform permits: a control the platform does not have, invented by the
    // web tier, which is the more damaging direction of the two.
    const session = openSession({ ...version, state: 'approved', editable: true });

    expect(session.sections.every((s) => s.editable)).toBe(true);
    session.edit('sec-1', [paragraph(text('A medicine for adults.'))]);
    expect(session.hasUnsavedWork).toBe(true);
  });

  it('marks a section it cannot represent as read-only, with the reason', () => {
    // Rather than opening it and silently dropping what it did not understand on save.
    const withTable = {
      ...version,
      sections: [
        {
          identity: 'sec-3',
          title: '3. Dosage',
          narrative:
            '<div xmlns="http://www.w3.org/1999/xhtml"><table><tr><td>10 mg</td></tr></table></div>',
        },
      ],
    };

    const section = openSession(withTable).sections[0]!;

    expect(section.editable).toBe(false);
    expect(section.readOnlyBecause).toMatch(/cannot represent/i);
  });

  it('leaves the rest of a label editable when one section cannot be represented', () => {
    // One section the surface cannot open must not make the whole label unauthorable.
    const mixed = {
      ...version,
      sections: [
        ...version.sections,
        { identity: 'sec-3', title: '3. Dosage', narrative: '<div xmlns="http://www.w3.org/1999/xhtml"><table/></div>' },
      ],
    };

    const session = openSession(mixed);

    expect(session.sections.filter((s) => s.editable).map((s) => s.identity)).toEqual([
      'sec-1',
      'sec-2',
    ]);
  });

  it('hands back what to save as narrative the write gate accepts', () => {
    const session = openSession(version);

    session.edit('sec-1', [paragraph(text('A medicine for adults.'))]);

    expect(session.toSections()).toEqual([
      {
        identity: 'sec-1',
        title: '1. What Examplinum is',
        narrative: narrative('A medicine for adults.'),
      },
      { identity: 'sec-2', title: '2. Before you take it', narrative: narrative('Read this.') },
    ]);
  });

  it('refuses to edit a section that is not in this version', () => {
    // A section identifier the platform did not send is one this surface invented.
    expect(() => openSession(version).edit('sec-9', [])).toThrow(/sec-9/);
  });

  it('offers the sections of this version as cross-reference targets, and not itself', () => {
    // What decision 3 needs: the author picks the section they mean. A section referring to
    // itself is not a cross-reference, it is a loop for whoever is reading.
    expect(openSession(version).crossReferenceTargetsFor('sec-1')).toEqual([
      { identity: 'sec-2', title: '2. Before you take it' },
    ]);
  });

  it('discards everything unsaved when asked, and says nothing was saved', () => {
    const session = openSession(version);
    session.edit('sec-1', [paragraph(text('Changed.'))]);

    session.discard();

    expect(session.hasUnsavedWork).toBe(false);
    expect(session.toSections()[0]!.narrative).toBe(narrative('A medicine.'));
  });
});
