# Security Policy

## Reporting a vulnerability
Please do **not** open public issues for security vulnerabilities. Instead, report them privately
to the project maintainers (add a security contact address here). We aim to acknowledge reports
promptly and will coordinate a fix and disclosure timeline with you.

## Scope and context
This platform handles regulated pharmaceutical product information under GxP / 21 CFR Part 11 /
EU Annex 11. Security controls (encryption, secrets management, RBAC/ABAC, tamper-evident audit,
WORM retention) are described in `design/D3-technical-architecture.md` Section 8 and capabilities
17-19 and 22 in `specs/capabilities/`.

## Development stack note
The `deploy/docker-compose` stack uses development-mode security (open ports, default credentials,
TLS off) and is for local development only. Never deploy it as-is.
