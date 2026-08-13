# Contributing

Thanks for your interest in contributing to the ePI Platform.

## Ground rules
- Be respectful; see `CODE_OF_CONDUCT.md`.
- By contributing, you agree your contributions are licensed under the Apache License 2.0
  (see `LICENSE`), per the inbound=outbound convention.
- This is regulated-domain software: correctness, traceability, and auditability matter.
  Prefer small, well-described changes with tests.

## How to contribute
1. Open an issue describing the change (bug, capability gap, or design proposal).
2. For non-trivial design changes, propose an **ADR** in `design/adrs/` before coding.
3. Fork and branch from `main` using a descriptive branch name.
4. Make your change with tests where applicable; keep documentation in sync.
5. Open a pull request referencing the issue; fill in the PR template.

## Conventions
- **Specifications** (`specs/`) and **architecture** (`design/`) are Markdown, ASCII-only,
  and the source of truth. Requirement IDs follow `CAP-<abbr>-NNN` (see the capability specs).
- **Commits**: clear, imperative subject lines; reference issues/ADRs.
- **Code**: follow the language conventions documented in each `src/` and `apps/` project.
- **Diagrams**: authored as Mermaid inline in the specs; exportable via `tools/`.

## Traceability
Changes that add or alter behaviour should map to a capability requirement (D2) and, where
architectural, an ADR (D3). Reference the relevant IDs in the PR.
