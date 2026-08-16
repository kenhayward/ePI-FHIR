# authoring-ui

The authoring and review surface (ADR-037). A client of the platform API.

## What it is responsible for, and what it is not

**It holds no governance logic.** Validation, segregation of duties, permitted transitions,
scope, signature requirements and immutability are decided by the platform and reflected here.
Nothing is enforced in this application, because a control implemented in a browser is not a
control - it is advice to a cooperative user, removable with the developer tools, and absent
entirely for anything calling the API directly.

The consequence to keep in mind while changing anything here: this surface may *disable* an
action, and the platform must still refuse it. Where the two disagree, the platform is right and
this has a defect.

**An author edits sections, never FHIR.** No Bundle, no resource type, no canonical URL reaches
the screen. There is a test that fails if one does.

**No identifier is ever typed.** Section identity, cross-reference targets, reusable-unit and
product references are chosen from something the platform resolves, and this surface writes the
identifier.

## Commands

Run from the repository root, which is the npm workspace:

```bash
npm install && npm test
```

`npm run lint` type-checks; `npm run dev` serves it; `npm run build` produces the static files
that sit behind the gateway.

## Layout

- `src/authoring/narrative.ts` - the formatting an author can produce, and the only thing that
  turns it into markup. Bounded to what the write gate accepts (ADR-037 decision 4).
- `src/authoring/editingSession.ts` - the working copy, held until the author saves.
- `src/LabelEditor.tsx` - the surface over it.
- `src/platform/client.ts` - the only way this application reaches the platform. It speaks
  sections, never FHIR, and carries the platform's refusals in the platform's own words.
- `src/platform/signIn.ts` - authorization code with PKCE against the identity provider. The
  access token lives in memory and is written nowhere, which costs a sign-in after every page
  refresh and means a cross-site scripting flaw has no stored token to read (ADR-039).

The model is deliberately separate from React and has no framework in it. It is where the rules
that matter live, it is tested without a DOM, and replacing the editing controls does not touch
it.

## What is not built yet

The placeholder this replaced named the whole of the surface's eventual job. Keeping that list
here so replacing the placeholder does not quietly shrink it:

- **Wiring.** `LabelEditor` still takes a version as a prop and calls `onSave`; nothing yet
  joins the sign-in, the client and the editor into an application with a page to land on. That
  is the next thing, and it is the last of it - every part exists and is tested.
- **In-context translation** (capability 9), side by side with the source.
- **Validation and compliance feedback** (11, 12) shown against the section that caused it.
- **Review and e-signature flows** (16, 19), which is where the segregation-of-duties rules
  become visible rather than merely enforced.
- **Reusable units and product selection** (2, 5). The platform has a product directory and no
  endpoint exposes it - the same kind of gap ADR-037 decision 7 expected building this to find,
  and the same kind that ADR-038 closed for sections.
- **Rich editing controls.** The narrative model carries emphasis, lists and cross-references;
  the editing control is a text area and can only produce paragraphs. It is deliberately the
  most bounded control there is rather than a rich-text component that would emit markup the
  write gate rejects.
