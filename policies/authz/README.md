# policies/authz/ - OPA / Rego authorization policies

Rego policies evaluated by Open Policy Agent (see `deploy/docker-compose`, service `opa`).
`example.rego` is a minimal starting policy demonstrating attribute-based scoping; replace with
the real policy set and add tests (`opa test`).
