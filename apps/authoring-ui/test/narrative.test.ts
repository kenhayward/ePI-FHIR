import { describe, expect, it } from 'vitest';
import {
  crossReference,
  emphasis,
  list,
  paragraph,
  parseNarrative,
  serialiseNarrative,
  text,
} from '../src/authoring/narrative';

// The narrative an author can produce, bounded to what validates (FN-AUT-001).
//   CAP-TPL-005 Template-driven guided authoring that shields authors from raw FHIR
//   CAP-SCM-003 Structured and narrative section content
//
// ADR-037 decision 4. A rich-text editor that can emit arbitrary HTML produces content that
// fails at the write gate after the author has finished writing it, and a save that silently
// rewrote what somebody wrote would be worse than one that refuses - in this domain the exact
// words are the point.
describe('FN-AUT-001 the narrative an author can write', () => {
  it('serialises to the XHTML div FHIR requires', () => {
    const written = serialiseNarrative([paragraph(text('Do not take more than two tablets.'))]);

    expect(written).toBe(
      '<div xmlns="http://www.w3.org/1999/xhtml">' +
        '<p>Do not take more than two tablets.</p>' +
        '</div>',
    );
  });

  it('comes back as it went in', () => {
    const written = [
      paragraph(text('Take '), emphasis('one'), text(' tablet daily.')),
      list(['With food.', 'With water.']),
    ];

    expect(parseNarrative(serialiseNarrative(written))).toEqual({ ok: true, blocks: written });
  });

  it('escapes what would otherwise change the markup', () => {
    // A label saying "under 6 & over 65" is a label, not a defect, and it must not become one.
    const written = serialiseNarrative([paragraph(text('under 6 & over 65 <see below>'))]);

    expect(written).toContain('under 6 &amp; over 65 &lt;see below&gt;');
    expect(parseNarrative(written)).toEqual({
      ok: true,
      blocks: [paragraph(text('under 6 & over 65 <see below>'))],
    });
  });

  it('writes a cross-reference from the section identity, never from typed text', () => {
    // ADR-028's debt and ADR-037 decision 3: the author picks the section they mean and the
    // surface writes the identifier. An author typing this by hand would be typing section
    // identifiers by hand.
    const written = serialiseNarrative([
      paragraph(text('See '), crossReference('sec-4-2', 'section 4.2'), text(' for warnings.')),
    ]);

    expect(written).toContain('<a href="#sec-4-2">section 4.2</a>');
  });

  it('keeps the anchor target when the text around it is rewritten', () => {
    const parsed = parseNarrative(
      '<div xmlns="http://www.w3.org/1999/xhtml"><p>See <a href="#sec-4-2">4.2</a>.</p></div>',
    );

    expect(parsed).toEqual({
      ok: true,
      blocks: [paragraph(text('See '), crossReference('sec-4-2', '4.2'), text('.'))],
    });
  });

  it('refuses content it cannot represent, rather than dropping it', () => {
    // Content can arrive from elsewhere - imported, migrated, produced by another system. A
    // parser that quietly discarded what it did not understand would let an author open a
    // section, save it, and silently delete part of a label.
    const parsed = parseNarrative(
      '<div xmlns="http://www.w3.org/1999/xhtml"><p>Dose</p><table><tr><td>10 mg</td></tr></table></div>',
    );

    expect(parsed.ok).toBe(false);
    expect(parsed.ok === false && parsed.unrepresentable).toContain('table');
  });

  it('names every element it could not represent, not just the first', () => {
    // So a reviewer reading the reason knows the size of the problem.
    const parsed = parseNarrative(
      '<div xmlns="http://www.w3.org/1999/xhtml"><table></table><img src="x.png"/></div>',
    );

    expect(parsed.ok === false && parsed.unrepresentable).toEqual(['img', 'table']);
  });

  it('refuses a div that is not the XHTML narrative div', () => {
    expect(parseNarrative('<p>Loose</p>').ok).toBe(false);
  });

  it('treats an empty narrative as empty rather than as a failure', () => {
    // A section an author has not written yet is normal, and must not present as broken.
    expect(parseNarrative('<div xmlns="http://www.w3.org/1999/xhtml"></div>')).toEqual({
      ok: true,
      blocks: [],
    });
  });

  it('cannot be given a cross-reference with no target', () => {
    // The identifier is what the reference is; text alone is a sentence about a section rather
    // than a reference to one.
    expect(() => crossReference('', 'section 4.2')).toThrow();
  });
});
