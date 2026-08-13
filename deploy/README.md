# deploy/ - Deployment assets

Everything needed to run the platform, following the progression in D3 Section 10.5:
**Docker Compose (dev) -> Kubernetes (test/prod) -> optional Azure/AKS**.

## Contents
- `docker-compose/` - the local development stack (all open-source backing services). Start here.
- `kubernetes/` - Kubernetes manifests / Helm charts for shared and production environments.
- `iac/` - Infrastructure as Code (OpenTofu) for provisioning clusters and managed dependencies.

Every component ships as a maintained container image, so the same artifacts run across all
environments (D3 Section 12).
