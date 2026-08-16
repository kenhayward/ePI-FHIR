import { useMemo, useState } from 'react';
import {
  type SectionDescription,
  type VersionDescription,
  openSession,
} from './authoring/editingSession';
import { SectionEditor } from './SectionEditor';
import type { Block } from './authoring/narrative';

/**
 * The sections of one label version, as an author edits them (ADR-037 decision 2).
 *
 * @remarks
 * Deliberately plain. Nothing here decides anything: whether a version may be written to is the
 * platform's answer, carried in the description, and the platform refuses the write regardless
 * (ADR-037 decision 1). What this component is responsible for is that an author is never
 * offered an action that will certainly fail, and never shown a Bundle.
 */
export function LabelEditor({
  version,
  onSave,
  alsoChanged = false,
  onSaveLabel,
}: {
  readonly version: VersionDescription;
  readonly onSave: (sections: readonly SectionDescription[]) => void;

  /**
   * Whether something outside the sections has changed - the product, today.
   *
   * @remarks
   * The session knows what has been typed and nothing else. Without this the save button stays
   * disabled after an author chooses a product, so they cannot save what they just chose, which
   * is how a surface teaches somebody that a control does not work.
   */
  readonly alsoChanged?: boolean;
  readonly onSaveLabel?: string;
}) {
  const session = useMemo(() => openSession(version), [version]);

  // The session holds the working copy; this only forces a render when it changes. Holding the
  // text in component state as well would be two copies of the same thing, and the one that
  // gets saved would eventually not be the one on the screen.
  //
  // The updater form is not a style preference. Written as setRevision(revision + 1) it reads
  // the value captured when the component rendered, so a burst of keystrokes all compute the
  // same next revision, React re-renders once, and every character but the last is lost from
  // the screen while sitting in the session. Caught by the test that types a sentence.
  const [, setRevision] = useState(0);

  const edit = (identity: string, blocks: readonly Block[]) => {
    session.edit(identity, blocks);
    setRevision((previous) => previous + 1);
  };

  return (
    <main>
      <h1>Editing a label</h1>
      {!version.editable && (
        <p role="status">
          You are not allowed to write to this label, so it is shown read-only.
        </p>
      )}

      {session.sections.map((section) => (
        <section key={section.identity}>
          <h2>{section.title}</h2>

          {section.editable ? (
            <SectionEditor
              title={section.title}
              blocks={section.blocks}
              targets={session.crossReferenceTargetsFor(section.identity)}
              onChange={(blocks) => edit(section.identity, blocks)}
            />
          ) : (
            <p role="note">{section.readOnlyBecause}</p>
          )}
        </section>
      ))}

      {/*
        Saying what saving does, because it is not what a text box usually does. No version is
        ever changed: this mints version {version.version + 1} and leaves the one on screen
        exactly as it was (ADR-038 decision 6).
      */}
      <button
        type="button"
        disabled={!session.hasUnsavedWork && !alsoChanged}
        onClick={() => onSave(session.toSections())}
      >
        {onSaveLabel ?? `Save as version ${version.version + 1}`}
      </button>
    </main>
  );
}
