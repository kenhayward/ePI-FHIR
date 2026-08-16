import { type Block, parseNarrative, serialiseNarrative } from './narrative';

/**
 * A version as the platform described it.
 *
 * @remarks
 * `editable` is the platform's answer, not this surface's opinion (ADR-037 decision 1). It means
 * "may this caller create the next version", not "may this version be changed" - no version may
 * ever be changed, and saving mints the next one (ADR-038 decision 6). So an approved version
 * opens like any other: drafting from one is how a label evolves, and a surface refusing it
 * would be disabling something the platform permits.
 */
export interface VersionDescription {
  readonly documentIdentifier: string;
  readonly version: number;
  readonly state: string;
  readonly editable: boolean;

  /** Which product the label is about, or null where it names none resolvably (ADR-040). */
  readonly product?: { readonly identifier: string; readonly display: string | null } | null;

  /**
   * What the state model permits from here, and which of those are signed gates.
   *
   * @remarks
   * The platform's answer. Deriving it here would be a second implementation of the state model
   * and the weaker of the two (ADR-037 decision 1) - and it is what the surface *offers*, never
   * what decides: every action is checked again on the way in.
   */
  readonly actions?: readonly string[];
  readonly signedActions?: readonly string[];
  readonly signatureMeanings?: Readonly<Record<string, string>>;
  readonly sections: readonly SectionDescription[];
}

export interface SectionDescription {
  readonly identity: string;
  readonly title: string;
  readonly narrative: string;
}

export interface OpenSection {
  readonly identity: string;
  readonly title: string;
  readonly blocks: readonly Block[];
  readonly editable: boolean;
  /** Why this section cannot be edited here, where it cannot. */
  readonly readOnlyBecause: string | null;
}

export interface CrossReferenceTarget {
  readonly identity: string;
  readonly title: string;
}

/**
 * The working copy of a version, held until the author saves (ADR-037 decision 6).
 *
 * @remarks
 * A version is immutable and minted on write, so anything saving as the author typed would mint
 * hundreds of them. The consequence, stated in the ADR and worth repeating where somebody will
 * read it: unsaved work lives in one browser until there is a draft workspace on the server.
 */
export class EditingSession {
  readonly #original: Map<string, string>;
  readonly #working: Map<string, readonly Block[]>;

  /**
   * What each section is, apart from its narrative.
   *
   * @remarks
   * The narrative deliberately is not here. It was, and that was a defect: editing updated the
   * working copy while this kept the text parsed when the session opened, so anything reading
   * `sections` showed the original however much had been typed. Two copies of one thing, and
   * the one on the screen was not the one that would be saved.
   */
  readonly #shape: readonly Omit<OpenSection, 'blocks'>[];
  readonly #description: VersionDescription;

  constructor(description: VersionDescription) {
    this.#description = description;
    this.#original = new Map();
    this.#working = new Map();
    this.#shape = description.sections.map((section) => {
      const parsed = parseNarrative(section.narrative);

      if (!parsed.ok) {
        // One section this surface cannot open must not make the whole label unauthorable, so
        // it is marked and the rest carries on.
        return {
          identity: section.identity,
          title: section.title,
          editable: false,
          readOnlyBecause: parsed.reason,
        };
      }

      this.#original.set(section.identity, section.narrative);
      this.#working.set(section.identity, parsed.blocks);

      return {
        identity: section.identity,
        title: section.title,
        editable: description.editable,
        readOnlyBecause: description.editable
          ? null
          : 'You are not allowed to write to this label.',
      };
    });
  }

  /** Every section, with the narrative as it stands in the working copy right now. */
  get sections(): readonly OpenSection[] {
    return this.#shape.map((section) => ({
      ...section,
      blocks: this.#working.get(section.identity) ?? [],
    }));
  }

  /** Sections whose narrative differs from what the platform sent. */
  get changed(): readonly OpenSection[] {
    return this.sections.filter(
      (section) =>
        this.#working.has(section.identity) &&
        serialiseNarrative(this.#working.get(section.identity)!) !==
          this.#original.get(section.identity),
    );
  }

  get hasUnsavedWork(): boolean {
    return this.changed.length > 0;
  }

  /** Replaces a section's narrative in the working copy. */
  edit(identity: string, blocks: readonly Block[]): void {
    const section = this.#shape.find((candidate) => candidate.identity === identity);

    if (section === undefined) {
      throw new Error(
        `There is no section '${identity}' in this version. A section identifier the platform ` +
          'did not send is one this surface invented.',
      );
    }

    if (!this.#description.editable) {
      throw new Error(
        'You are not allowed to write to this label. That is the platform\'s answer, carried ' +
          'in the version it sent, and it would refuse the write in any case.',
      );
    }

    if (!section.editable) {
      throw new Error(section.readOnlyBecause ?? `Section '${identity}' cannot be edited here.`);
    }

    this.#working.set(identity, blocks);
  }

  /** Puts every section back as the platform sent it. */
  discard(): void {
    for (const [identity, narrative] of this.#original) {
      const parsed = parseNarrative(narrative);
      if (parsed.ok) {
        this.#working.set(identity, parsed.blocks);
      }
    }
  }

  /**
   * Every section that another may refer to, which is every section but this one.
   *
   * @remarks
   * What ADR-037 decision 3 needs: the author picks the section they mean and the surface writes
   * the identifier. A section offered as a target for itself would be a loop for whoever is
   * reading it.
   */
  crossReferenceTargetsFor(identity: string): readonly CrossReferenceTarget[] {
    return this.#shape
      .filter((section) => section.identity !== identity)
      .map((section) => ({ identity: section.identity, title: section.title }));
  }

  /** What to send, as narrative the write gate accepts. */
  toSections(): readonly SectionDescription[] {
    return this.#description.sections.map((section) => {
      const working = this.#working.get(section.identity);

      return {
        identity: section.identity,
        title: section.title,

        // A section this surface could not open is sent back exactly as it arrived, byte for
        // byte. Re-serialising something it does not fully understand is how a save quietly
        // rewrites a label.
        narrative: working === undefined ? section.narrative : serialiseNarrative(working),
      };
    });
  }
}

export const openSession = (description: VersionDescription): EditingSession =>
  new EditingSession(description);
