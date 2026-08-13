using System.Net;
using System.Text;
using Xunit;

namespace Epi.Iam.Tests;

// FN-IAM-003 Evaluate the policy decision and enforce allow or deny.
// The wire behaviour, against a stubbed policy server: what the platform does with what OPA
// says, including what it does when OPA says nothing useful.
public sealed class OpaPolicyDecisionPointTests
{
    private static readonly AuthorizationQuery Query = new(
        new Subject("user-anna", ["affiliate_author"], ["uk-affiliate"], ["GB"]),
        "author",
        new ResourceScope("uk-affiliate", "GB"));

    private static OpaPolicyDecisionPoint Responding(HttpStatusCode status, string body, Action<string>? captureRequest = null)
    {
        var handler = new StubHandler(status, body, captureRequest);
        return new OpaPolicyDecisionPoint(new HttpClient(handler) { BaseAddress = new Uri("http://opa.test") });
    }

    [Fact]
    public async Task FN_IAM_003_an_allow_decision_is_allowed()
    {
        var pdp = Responding(HttpStatusCode.OK, """{"result": "allow"}""");

        var decision = await pdp.DecideAsync(Query);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task FN_IAM_003_a_deny_decision_is_denied_with_a_reason()
    {
        var pdp = Responding(HttpStatusCode.OK, """{"result": "deny"}""");

        var decision = await pdp.DecideAsync(Query);

        Assert.False(decision.Allowed);
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
    }

    [Fact]
    public async Task FN_IAM_003_an_undefined_result_is_denied()
    {
        // OPA returns an empty document when the rule is undefined. Reading that as anything
        // other than deny would turn a policy gap into open access.
        var pdp = Responding(HttpStatusCode.OK, "{}");

        var decision = await pdp.DecideAsync(Query);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task FN_IAM_003_an_unreachable_or_failing_policy_server_is_denied()
    {
        // Fail closed. An authorisation service that opens the door when its policy engine is
        // down is worse than no authorisation service, because it looks like one.
        var pdp = Responding(HttpStatusCode.InternalServerError, "boom");

        var decision = await pdp.DecideAsync(Query);

        Assert.False(decision.Allowed);
        Assert.Contains("policy", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FN_IAM_002_the_query_sent_matches_the_policy_input_contract()
    {
        string? sent = null;
        var pdp = Responding(HttpStatusCode.OK, """{"result": "allow"}""", body => sent = body);

        await pdp.DecideAsync(Query);

        Assert.NotNull(sent);
        // The shape policies/authz/example.rego reads: input.subject.roles, input.action,
        // input.resource.affiliate, input.resource.market.
        Assert.Contains("\"input\"", sent!);
        Assert.Contains("\"roles\"", sent);
        Assert.Contains("\"action\"", sent);
        Assert.Contains("\"affiliate\"", sent);
        Assert.Contains("\"market\"", sent);
    }

    private sealed class StubHandler(HttpStatusCode status, string body, Action<string>? capture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture?.Invoke(await (request.Content?.ReadAsStringAsync(cancellationToken) ?? Task.FromResult("")));
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
