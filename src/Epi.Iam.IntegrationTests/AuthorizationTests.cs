using Xunit;

namespace Epi.Iam.IntegrationTests;

/// <summary>
/// The platform's authorisation calls, decided by the real policy in a real OPA.
///   IT-002 A caller outside the resource affiliate or market is denied
/// </summary>
[Collection(OpaCollection.Name)]
[Trait("Category", "Container")]
public sealed class AuthorizationTests(OpaServer opa)
{
    private static readonly ResourceScope UkLabel = new("uk-affiliate", "GB", Author: "user-anna");

    private static Subject Author(string id = "user-anna") =>
        new(id, ["affiliate_author"], ["uk-affiliate"], ["GB"]);

    private IPolicyDecisionPoint Pdp() => new OpaPolicyDecisionPoint(opa.CreateClient());

    [Fact]
    public async Task IT_002_a_subject_in_scope_with_the_right_role_is_allowed()
    {
        var decision = await Pdp().DecideAsync(new AuthorizationQuery(Author(), "author", UkLabel));

        Assert.True(decision.Allowed, decision.Reason);
    }

    [Fact]
    public async Task IT_002_a_subject_from_another_affiliate_is_denied()
    {
        var outsider = new Subject("user-dirk", ["affiliate_author"], ["de-affiliate"], ["GB"]);

        var decision = await Pdp().DecideAsync(new AuthorizationQuery(outsider, "author", UkLabel));

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task IT_002_a_subject_without_the_market_in_scope_is_denied()
    {
        var outsider = new Subject("user-elke", ["affiliate_author"], ["uk-affiliate"], ["DE"]);

        var decision = await Pdp().DecideAsync(new AuthorizationQuery(outsider, "author", UkLabel));

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task IT_002_a_role_without_the_action_is_denied()
    {
        var reader = new Subject("user-cara", ["reader"], ["uk-affiliate"], ["GB"]);

        var decision = await Pdp().DecideAsync(new AuthorizationQuery(reader, "author", UkLabel));

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task IT_002_segregation_of_duties_denies_an_author_approving_their_own_label()
    {
        // The same rule the Rego unit tests cover, reached through the platform's own call
        // path: the input contract and the policy agree, not just the policy with itself.
        var approver = new Subject("user-anna", ["affiliate_approver"], ["uk-affiliate"], ["GB"]);

        var decision = await Pdp().DecideAsync(new AuthorizationQuery(approver, "approve", UkLabel));

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task IT_002_an_independent_approver_is_allowed()
    {
        var approver = new Subject("user-ben", ["affiliate_approver"], ["uk-affiliate"], ["GB"]);

        var decision = await Pdp().DecideAsync(new AuthorizationQuery(approver, "approve", UkLabel));

        Assert.True(decision.Allowed, decision.Reason);
    }
}
