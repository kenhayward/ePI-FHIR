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

`npm run dev` serves it for development; `npm run lint` type-checks; `npm run build` produces the
static files the container serves.

**Where it is pointed is read at start-up, not at build time** (ADR-049). It fetches `config.json`
from beside itself and refuses to render without a complete one, rather than defaulting to
localhost and failing somewhere nobody would attribute to configuration. In the development stack
that file is `config/ui/authoring.json`, mounted into the container; for `npm run dev` there is a
copy at `public/config.json`.

The addresses in it are the browser's. A browser resolves `localhost` and cannot reach `epi-api`
or `keycloak` whatever the container serving these files can.

The whole application is in the stack: `cd deploy/docker-compose && docker compose up -d` serves
it at http://localhost:5173.

Sign in as one of the demonstration people - `user-anna`, `user-ben`, `user-rae`, `user-ops`, all
with password `Demo-Passw0rd!`. **Not** Keycloak's `admin`, which belongs to the `master` realm and
is unknown to this surface.

The address is where you are (ADR-051). Opening a label puts it in the address, so it can be
bookmarked, shared and reloaded, and the browser's back button returns to the search. A label
address followed while signed out survives the round trip to the identity provider: where you were
going is remembered locally rather than handed to the provider, because a label identifier is the
platform's business and not its.

## Layout

- `src/authoring/narrative.ts` - the formatting an author can produce, and the only thing that
  turns it into markup. Bounded to what the write gate accepts (ADR-037 decision 4).
- `src/authoring/editingSession.ts` - the working copy, held until the author saves.
- `src/App.tsx` - the application: sign in, open the label the address names, edit, save. Most
  of it is about outcomes, because a refusal that reaches no screen is a refusal that did not
  happen as far as the author is concerned.
- `src/LabelPicker.tsx` - finding a label. Its wording is load-bearing: a scoped search leaves
  "no such label" and "not one you may see" genuinely indistinguishable, and the platform refuses
  to resolve that, so neither does this.
- `src/WaitingWork.tsx` - what routing has asked of this person. A failure is never shown as an
  empty list: "nothing is waiting" is a claim, and saying it when nobody knows tells somebody
  their work is done.
- `src/LifecycleActions.tsx` - submitting, approving, and the one password field in this
  application. It is there because signing a record is not signing in (ADR-041): re-entering
  credentials is what makes a signature attributable to a person rather than to a session
  somebody left open.
- `src/VersionHistory.tsx` - what happened, who did it, what they signed and what it was approved
  against. The one thing on it that must never be subtle is a pinned package whose bytes no
  longer match: that is tamper-evidence firing (ADR-023).
- `src/LeafletPreview.tsx` - the leaflet the content produces. The rendered HTML goes into a
  sandboxed frame and never into this page: it is the platform's own output and still a document
  assembled from what people type, and a leaflet needs none of this page's origin, session or
  token.
- `src/MarketApprovals.tsx` - where each market stands. Two rules meet here and must not blur:
  submitting to a regulator is signed and recording its decision is not (CAP-LCM-012), and only
  the action that records an approval may say when it takes effect (ADR-029).
- `src/ProductChoice.tsx` - which product the label is about, chosen rather than typed. The
  identifier is never shown: it is what the platform stores and resolves, and it means nothing
  to the person choosing.
- `src/LabelEditor.tsx` - the surface over the working copy.
- `src/SectionEditor.tsx` - writing one section. Bounded by construction rather than corrected
  afterwards: paragraphs, lists, emphasis and references, which is the whole set the narrative
  model can express. A paragraph is edited as its parts rather than as one string, so an edit to
  the words around a reference cannot take the reference with it.
- `src/authoring/runEditing.ts` - marking part of a sentence. Pure functions over the model,
  because an off-by-one here loses a character out of a regulated sentence.
- `src/platform/client.ts` - the only way this application reaches the platform. It speaks
  sections, never FHIR, and carries the platform's refusals in the platform's own words.
- `src/platform/signIn.ts` - authorization code with PKCE against the identity provider. The
  access token lives in memory and is written nowhere, which costs a sign-in after every page
  refresh and means a cross-site scripting flaw has no stored token to read (ADR-039).

The model is deliberately separate from React and has no framework in it. It is where the rules
that matter live, it is tested without a DOM, and replacing the editing controls does not touch
it.

## Who this is for

**An author.** Where a screen could serve an author or an inspector, it serves the author.

That is a decision rather than an accident, and it decides real things. The version history shows
what happened and what was signed, and it does not distinguish a pin taken before terminology was
recorded from an approval that was asked and had none - a distinction ADR-036 decision 3 keeps in
the record deliberately, and one an inspector would want and an author would read as noise.

The consequence is that **inspection wants its own surface**, reading the same records with
different questions in mind. It is not built and it is not this.

## What is not built yet

- **An official render.** The preview is rendered with scaffolding, not with a template anybody
  approved - and ADR-033 decision 2 says a render template is content, versioned and approved,
  because it determines what a patient reads. Until there is a template store there is nothing to
  render an official artefact *with*, and nothing is written to the asset store.

The placeholder this replaced named the whole of the surface's eventual job. Keeping that list
here so replacing the placeholder does not quietly shrink it:

- **Paging through search results.** The platform pages and this shows the first page with an
  honest count of what it is not showing. Somebody looking for the thirtieth label cannot reach
  it yet.
- **Choosing a reusable unit.** The same shape as picking a product, and the platform exposes no
  endpoint for one - ADR-026 settled what a unit is, not how an author reaches one.
- **Nothing has run against a real Keycloak or a real API.** Every part is tested against a
  fake, which is the right way to test them and no substitute for the redirect actually
  happening.
- **In-context translation** (capability 9), side by side with the source.
- **Validation and compliance feedback** (11, 12) shown against the section that caused it.
- **Review and e-signature flows** (16, 19), which is where the segregation-of-duties rules
  become visible rather than merely enforced.
- **Reusable units and product selection** (2, 5). The platform has a product directory and no
  endpoint exposes it - the same kind of gap ADR-037 decision 7 expected building this to find,
  and the same kind that ADR-038 closed for sections.
