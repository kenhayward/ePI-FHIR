using System.Security.Claims;
using Xunit;

namespace Epi.Iam.Tests;

// FN-IAM-001 Validate an OIDC access token and extract subject claims.
// Token validation itself is the framework's job and is configured at the host; what is ours,
// and what can silently go wrong, is turning a validated principal into a scoped subject.
public sealed class SubjectFactoryTests
{
    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    [Fact]
    public void FN_IAM_001_reads_identity_roles_and_scope_from_an_authenticated_principal()
    {
        var principal = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, "user-anna"),
            new Claim(SubjectFactory.RolesClaim, "affiliate_author"),
            new Claim(SubjectFactory.AffiliatesClaim, "uk-affiliate"),
            new Claim(SubjectFactory.MarketsClaim, "GB"),
            new Claim(SubjectFactory.MarketsClaim, "IE"));

        var subject = SubjectFactory.From(principal);

        Assert.NotNull(subject);
        Assert.Equal("user-anna", subject!.Id);
        Assert.Equal(["affiliate_author"], subject.Roles);
        Assert.Equal(["uk-affiliate"], subject.Affiliates);
        Assert.Equal(["GB", "IE"], subject.Markets);
    }

    [Fact]
    public void FN_IAM_001_accepts_sub_as_the_identifier_when_the_mapped_claim_is_absent()
    {
        var subject = SubjectFactory.From(Authenticated(new Claim("sub", "user-ben")));

        Assert.Equal("user-ben", subject?.Id);
    }

    [Fact]
    public void FN_IAM_001_an_unauthenticated_principal_is_nobody()
    {
        Assert.Null(SubjectFactory.From(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(SubjectFactory.From(null));
    }

    [Fact]
    public void FN_IAM_001_a_token_without_a_subject_identifier_is_nobody()
    {
        var principal = Authenticated(new Claim(SubjectFactory.RolesClaim, "administrator"));

        Assert.Null(SubjectFactory.From(principal));
    }

    [Fact]
    public void FN_IAM_001_a_token_carrying_no_scope_yields_empty_scope_not_unrestricted_access()
    {
        // Least privilege by default (CAP-IAM-007). Treating an absent scope claim as
        // "everything" is how a misconfigured client ends up reading every affiliate.
        var subject = SubjectFactory.From(Authenticated(new Claim(ClaimTypes.NameIdentifier, "user-cara")));

        Assert.NotNull(subject);
        Assert.Empty(subject!.Affiliates);
        Assert.Empty(subject.Markets);
        Assert.Empty(subject.Roles);
    }

    [Fact]
    public void FN_IAM_001_the_subject_carries_the_name_the_person_signs_in_as()
    {
        // Distinct from the subject identifier and needed alongside it: an identity provider
        // authenticates a username and attributes a subject, so re-checking a password at a
        // signing gate needs the name they type. Passing the subject there produced a refusal
        // that read exactly like a wrong password.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "9876db89-eab3-42ff-82b0-a23fa9b296ce"),
                new Claim("preferred_username", "user-anna"),
            ],
            "test"));

        var subject = SubjectFactory.From(principal);

        Assert.Equal("9876db89-eab3-42ff-82b0-a23fa9b296ce", subject!.Id);
        Assert.Equal("user-anna", subject.Username);
    }

    [Fact]
    public void FN_IAM_001_a_provider_that_issues_no_username_falls_back_to_the_subject()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "018f2a10-anna")], "test"));

        Assert.Equal("018f2a10-anna", SubjectFactory.From(principal)!.Username);
    }
}
