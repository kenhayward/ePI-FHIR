# apps/ - Frontend applications

User-facing applications (TypeScript / React), per D3 Section 12.

## Contents
- `authoring-ui/` - the authoring and review web application: template-driven guided authoring
  (capability 3), review/approval (16), validation/compliance feedback (11/12), localisation (9).

Apps consume the backend via the API gateway (capability 15 / D3 Section 5). Keep the UI free of
raw FHIR complexity - drive it from templates and terminology bindings.
