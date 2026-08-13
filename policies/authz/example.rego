package epi.authz

# Minimal illustrative RBAC/ABAC policy (capability 17). Not production policy.
# Decision: allow if the subject has a role granting the action AND the subject's scope
# covers the resource's affiliate and market.
# Note: "if" and "in" are keywords by default in OPA 1.x, so no future imports.

default allow := false

allow if {
	role_allows_action
	scope_covers_resource
}

role_allows_action if {
	some role in input.subject.roles
	input.action in data.roles[role].actions
}

scope_covers_resource if {
	input.resource.affiliate in input.subject.scope.affiliates
	input.resource.market in input.subject.scope.markets
}

# Segregation of duties: an author cannot approve their own content.
deny_sod if {
	input.action == "approve"
	input.resource.author == input.subject.id
}

# Effective decision (deny SoD overrides allow).
decision := "deny" if deny_sod
decision := "allow" if {
	not deny_sod
	allow
}
decision := "deny" if {
	not deny_sod
	not allow
}
