package epi.authz_test

# Tests for the illustrative RBAC/ABAC policy (capability 17, D3 Section 8.1).
# They pin the three things the policy is meant to guarantee: a role must grant
# the action (RBAC), the subject's scope must cover the resource's affiliate and
# market (ABAC), and segregation of duties must override an otherwise valid
# allow (capability 19, D3 Section 7.4).
#
# These document the behaviour of a placeholder policy. When the real policy set
# replaces example.rego, the cases below are the floor it has to keep clearing.

import data.epi.authz

# Role catalogue the policy resolves through data.roles.
roles := {
	"affiliate_author": {"actions": ["read", "author"]},
	"affiliate_approver": {"actions": ["read", "approve"]},
	"reader": {"actions": ["read"]},
}

uk_scope := {"affiliates": ["uk-affiliate"], "markets": ["GB"]}

# Anna authored the label; Ben is an independent approver in the same scope.
anna_author := {"id": "user-anna", "roles": ["affiliate_author"], "scope": uk_scope}

anna_approver := {"id": "user-anna", "roles": ["affiliate_approver"], "scope": uk_scope}

ben_approver := {"id": "user-ben", "roles": ["affiliate_approver"], "scope": uk_scope}

reader := {"id": "user-cara", "roles": ["reader"], "scope": uk_scope}

uk_label := {"affiliate": "uk-affiliate", "market": "GB", "author": "user-anna"}

# RBAC: the role grants the action, and the scope covers the resource.
test_author_in_scope_may_author if {
	inp := {"subject": anna_author, "action": "author", "resource": uk_label}
	authz.decision == "allow" with input as inp with data.roles as roles
}

# RBAC: a role that does not grant the action is denied.
test_role_without_the_action_is_denied if {
	inp := {"subject": reader, "action": "author", "resource": uk_label}
	authz.decision == "deny" with input as inp with data.roles as roles
}

# ABAC: right role, wrong affiliate.
test_affiliate_outside_scope_is_denied if {
	de_label := {"affiliate": "de-affiliate", "market": "GB", "author": "user-anna"}
	inp := {"subject": anna_author, "action": "author", "resource": de_label}
	authz.decision == "deny" with input as inp with data.roles as roles
}

# ABAC: right role and affiliate, wrong market.
test_market_outside_scope_is_denied if {
	de_market_label := {"affiliate": "uk-affiliate", "market": "DE", "author": "user-anna"}
	inp := {"subject": anna_author, "action": "author", "resource": de_market_label}
	authz.decision == "deny" with input as inp with data.roles as roles
}

# Segregation of duties: the author cannot approve their own label even though
# the approver role and the scope would otherwise allow it.
test_author_may_not_approve_own_label if {
	inp := {"subject": anna_approver, "action": "approve", "resource": uk_label}
	authz.decision == "deny" with input as inp with data.roles as roles
}

# The same request from an independent approver is allowed, which proves the
# previous case fails on segregation of duties and not on role or scope.
test_independent_approver_may_approve if {
	inp := {"subject": ben_approver, "action": "approve", "resource": uk_label}
	authz.decision == "allow" with input as inp with data.roles as roles
}

# Deny by default: no subject, no action, no resource.
test_empty_input_is_denied if {
	authz.decision == "deny" with input as {} with data.roles as roles
	authz.allow == false with input as {} with data.roles as roles
}
