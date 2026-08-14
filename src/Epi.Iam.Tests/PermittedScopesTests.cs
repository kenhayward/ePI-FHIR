using Epi.ContentCore;
using Xunit;

namespace Epi.Iam.Tests;

// FN-IAM-005 Resolve the scopes a caller may search within, from the policy that decides a read
//   CAP-SCH-004 Scope all results by caller permissions; never leak out-of-scope content
public sealed class PermittedScopesTests
{
    private static Subject Caller(string[] affiliates, string[] markets) =>
        new("user-rae", ["regulatory"], affiliates, markets);

    [Fact]
    public async Task FN_IAM_005_the_candidates_are_the_cross_product_of_affiliate_and_market()
    {
        var policy = new RecordingPolicy(allow: true);
        var scopes = await new PolicyPermittedScopes(policy).ForAsync(
            Caller(["uk-affiliate", "eu-affiliate"], ["GB", "EU"]), "read");

        Assert.Equal(4, scopes.Count);
        Assert.Contains(new DocumentScope("uk-affiliate", "GB"), scopes);
        Assert.Contains(new DocumentScope("eu-affiliate", "EU"), scopes);
    }

    [Fact]
    public async Task FN_IAM_005_a_scope_the_policy_denies_is_not_permitted()
    {
        // The point of asking at all. An identity asserting a market says what the caller
        // claims; the policy says what the caller may do with it.
        var policy = new RecordingPolicy(
            allow: query => query.Resource.Market == "GB");

        var scopes = await new PolicyPermittedScopes(policy).ForAsync(
            Caller(["uk-affiliate"], ["GB", "EU"]), "read");

        Assert.Equal([new DocumentScope("uk-affiliate", "GB")], scopes);
    }

    [Fact]
    public async Task CAP_SCH_004_the_policy_decides_rather_than_the_resolver()
    {
        // A resolver that read the subject's attributes and decided for itself would be a
        // second implementation of scope_covers_resource, in a language no Rego test reaches
        // (ADR-022 decision 4). A policy denying everything must leave nothing permitted even
        // though the identity asserts both scopes.
        var policy = new RecordingPolicy(allow: false);

        var scopes = await new PolicyPermittedScopes(policy).ForAsync(
            Caller(["uk-affiliate"], ["GB"]), "read");

        Assert.Empty(scopes);
        Assert.NotEmpty(policy.Queries);
    }

    [Fact]
    public async Task CAP_SCH_004_a_caller_asserting_no_scope_is_permitted_nothing()
    {
        // Not "no restriction". An empty result here becomes an empty predicate downstream if
        // anyone treats it as absence, which is how this class of code fails.
        var policy = new RecordingPolicy(allow: true);

        Assert.Empty(await new PolicyPermittedScopes(policy).ForAsync(
            Caller([], ["GB"]), "read"));
        Assert.Empty(await new PolicyPermittedScopes(policy).ForAsync(
            Caller(["uk-affiliate"], []), "read"));
    }

    [Fact]
    public async Task FN_IAM_005_the_action_asked_about_is_the_action_the_caller_named()
    {
        // Scope for reading is not scope for approving. Resolving one and using it for the
        // other would widen a caller's reach by the difference between two policy rules.
        var policy = new RecordingPolicy(allow: true);

        await new PolicyPermittedScopes(policy).ForAsync(Caller(["uk-affiliate"], ["GB"]), "approve");

        Assert.Equal("approve", Assert.Single(policy.Queries).Action);
    }

    private sealed class RecordingPolicy : IPolicyDecisionPoint
    {
        private readonly Func<AuthorizationQuery, bool> _allow;

        public RecordingPolicy(bool allow) => _allow = _ => allow;

        public RecordingPolicy(Func<AuthorizationQuery, bool> allow) => _allow = allow;

        public List<AuthorizationQuery> Queries { get; } = [];

        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(_allow(query)
                ? new AuthorizationDecision(true, "stub")
                : AuthorizationDecision.Deny("stub"));
        }
    }
}
