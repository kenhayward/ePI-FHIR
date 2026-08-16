/**
 * The narrative an author can write, and nothing else (ADR-037 decision 4).
 *
 * ePI narrative is XHTML, and a rich-text editor that can emit arbitrary HTML produces content
 * that fails at the write gate after the author has finished writing it. So the permitted
 * formatting is a small declared set, and this module is the only thing that turns it into
 * markup. Whatever the editing controls are, they cannot produce anything this cannot express.
 *
 * The set is deliberately smaller than XHTML allows. It is what a package leaflet actually uses,
 * and widening it is a decision to be taken with the write gate in view rather than an omission
 * to be filled in.
 */

export const NARRATIVE_NAMESPACE = 'http://www.w3.org/1999/xhtml';

/** A run of plain words. */
export interface TextRun {
  readonly kind: 'text';
  readonly value: string;
}

/** Words the author has emphasised. */
export interface EmphasisRun {
  readonly kind: 'emphasis';
  readonly value: string;
}

/**
 * A reference to another section, carrying the identity the author chose.
 *
 * The target is a section identifier and is never typed (ADR-028, ADR-037 decision 3). The label
 * is what a reader sees and may be rewritten freely; the target is what makes it a reference.
 */
export interface CrossReferenceRun {
  readonly kind: 'crossReference';
  readonly target: string;
  readonly value: string;
}

export type Run = TextRun | EmphasisRun | CrossReferenceRun;

export interface Paragraph {
  readonly kind: 'paragraph';
  readonly runs: readonly Run[];
}

export interface BulletList {
  readonly kind: 'list';
  readonly items: readonly string[];
}

export type Block = Paragraph | BulletList;

export const text = (value: string): TextRun => ({ kind: 'text', value });

export const emphasis = (value: string): EmphasisRun => ({ kind: 'emphasis', value });

export const crossReference = (target: string, value: string): CrossReferenceRun => {
  if (target.trim() === '') {
    throw new Error(
      'A cross-reference must name the section it refers to. Text alone is a sentence about a ' +
        'section rather than a reference to one, and nothing would resolve it (ADR-028).',
    );
  }

  return { kind: 'crossReference', target, value };
};

export const paragraph = (...runs: readonly Run[]): Paragraph => ({ kind: 'paragraph', runs });

export const list = (items: readonly string[]): BulletList => ({ kind: 'list', items });

/** What a narrative parsed to, or what stopped it. */
export type ParsedNarrative =
  | { readonly ok: true; readonly blocks: readonly Block[] }
  /**
   * Refused rather than degraded. Content can arrive from elsewhere - imported, migrated,
   * produced by another system - and a parser that quietly discarded what it did not understand
   * would let an author open a section, save it, and silently delete part of a label.
   */
  | { readonly ok: false; readonly reason: string; readonly unrepresentable: readonly string[] };

const escape = (value: string): string =>
  value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');

const serialiseRun = (run: Run): string => {
  switch (run.kind) {
    case 'text':
      return escape(run.value);
    case 'emphasis':
      return `<em>${escape(run.value)}</em>`;
    case 'crossReference':
      return `<a href="#${escape(run.target)}">${escape(run.value)}</a>`;
  }
};

const serialiseBlock = (block: Block): string => {
  switch (block.kind) {
    case 'paragraph':
      return `<p>${block.runs.map(serialiseRun).join('')}</p>`;
    case 'list':
      return `<ul>${block.items.map((item) => `<li>${escape(item)}</li>`).join('')}</ul>`;
  }
};

/** Turns what an author wrote into the XHTML div FHIR requires. */
export const serialiseNarrative = (blocks: readonly Block[]): string =>
  `<div xmlns="${NARRATIVE_NAMESPACE}">${blocks.map(serialiseBlock).join('')}</div>`;

const RUN_ELEMENTS = new Set(['em', 'a']);
const BLOCK_ELEMENTS = new Set(['p', 'ul']);

/**
 * Reads a narrative back, or says what it could not represent.
 *
 * @remarks
 * Parsed with the browser's own XML parser rather than a regular expression, because the input
 * is content somebody else produced and the failure mode of getting that wrong is silently
 * mangling a label.
 */
export const parseNarrative = (xhtml: string): ParsedNarrative => {
  const parsed = new DOMParser().parseFromString(xhtml, 'application/xhtml+xml');

  if (parsed.getElementsByTagName('parsererror').length > 0) {
    return {
      ok: false,
      reason: 'This section is not well-formed XHTML, so it cannot be edited here.',
      unrepresentable: [],
    };
  }

  const root = parsed.documentElement;
  if (root.localName !== 'div' || root.namespaceURI !== NARRATIVE_NAMESPACE) {
    return {
      ok: false,
      reason:
        'This section is not a FHIR narrative div, so it cannot be edited here. FHIR requires ' +
        `narrative to be a <div> in ${NARRATIVE_NAMESPACE}.`,
      unrepresentable: [],
    };
  }

  const unrepresentable = new Set<string>();
  const blocks: Block[] = [];

  for (const child of Array.from(root.children)) {
    if (!BLOCK_ELEMENTS.has(child.localName)) {
      unrepresentable.add(child.localName);
      continue;
    }

    if (child.localName === 'ul') {
      const items: string[] = [];
      for (const item of Array.from(child.children)) {
        if (item.localName === 'li' && item.children.length === 0) {
          items.push(item.textContent ?? '');
        } else {
          unrepresentable.add(item.localName);
        }
      }

      blocks.push(list(items));
      continue;
    }

    const runs: Run[] = [];
    for (const node of Array.from(child.childNodes)) {
      if (node.nodeType === node.TEXT_NODE) {
        runs.push(text(node.textContent ?? ''));
        continue;
      }

      if (node.nodeType !== node.ELEMENT_NODE) {
        continue;
      }

      const element = node as Element;
      if (!RUN_ELEMENTS.has(element.localName) || element.children.length > 0) {
        unrepresentable.add(element.localName);
        continue;
      }

      if (element.localName === 'em') {
        runs.push(emphasis(element.textContent ?? ''));
        continue;
      }

      const href = element.getAttribute('href') ?? '';
      if (!href.startsWith('#')) {
        // A link out of the document is not a cross-reference, and this surface has no way to
        // represent one - so it is named rather than turned into plain text.
        unrepresentable.add('a');
        continue;
      }

      runs.push(crossReference(href.slice(1), element.textContent ?? ''));
    }

    blocks.push(paragraph(...runs));
  }

  if (unrepresentable.size > 0) {
    const named = [...unrepresentable].sort();
    return {
      ok: false,
      reason:
        'This section contains formatting this surface cannot represent, so editing it here ' +
        `would lose it: ${named.join(', ')}.`,
      unrepresentable: named,
    };
  }

  return { ok: true, blocks };
};
