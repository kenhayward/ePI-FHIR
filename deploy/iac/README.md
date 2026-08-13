# deploy/iac/

Infrastructure as Code (OpenTofu) for provisioning environments - clusters, networking, managed
data services, object storage, and secrets - per D3 Section 10 and ADR-014.

Placeholder. Organise by environment (e.g. `envs/dev`, `envs/test`, `envs/prod`) and by module
(cluster, database, object-store, messaging, identity). Keep provider choice pluggable so the
open-source/on-prem target and the future Azure target share module interfaces.
