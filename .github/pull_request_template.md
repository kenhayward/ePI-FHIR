# Summary

<!-- What changes and why, in a sentence or two. -->

## Traceability

<!-- Regulated-domain software: every behavioural change traces to a requirement. -->

- Capability requirement(s): <!-- e.g. CAP-MDM-003, or "n/a - documentation only" -->
- ADR(s): <!-- e.g. ADR-007, or "n/a" -->
- Issue: <!-- e.g. Closes #12 -->

## Test-driven development

<!-- This repository is test-driven. The failing test comes first, in its own commit. -->

- [ ] Tests were written **before** the implementation and observed **failing** (red)
- [ ] Link to the red run or commit: <!-- CI run URL, or the test-only commit SHA -->
- [ ] The same tests now pass (green), with no test weakened or deleted to achieve it
- [ ] Acceptance criteria from the relevant D2 capability section are covered

<!-- Documentation/spec-only PRs: state that here and tick the boxes as n/a. -->

## Type of change

- [ ] Specification (`specs/`) - the *what*
- [ ] Architecture / ADR (`design/`) - the *how*
- [ ] Application code (`src/`, `apps/`)
- [ ] Configuration as data (`config/`, `policies/`, `profiles/`)
- [ ] Deployment (`deploy/`) or tooling (`tools/`)

## Checklist

- [ ] CI is green on GitHub-hosted runners (the authoritative gate)
- [ ] `specs/` and `design/` changes are ASCII-only and consistent across D1/D2/D3
- [ ] A new market, regulator, or rule is achieved by configuration, not a code release
- [ ] No real product or personal data in fixtures - test data is synthetic
- [ ] Documentation updated alongside the change

## Reviewer notes

<!-- Anything the human reviewer should look at first, and anything you are unsure about. -->

---

<!-- Merges require human approval. Do not self-merge. -->
