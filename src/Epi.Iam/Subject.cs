using System.Security.Claims;

namespace Epi.Iam;

/// <summary>
/// Who is asking, and what they are scoped to (capability 17). Built from the tokens the
/// enterprise identity provider issues; the platform never authenticates anyone itself.
/// </summary>
/// <param name="Id">
/// The subject the identity provider assigns. Everything the platform attributes - audit
/// records, authorship, signatures - uses this, because it is the only identifier that cannot
/// be reassigned to somebody else later.
/// </param>
/// <param name="Username">
/// What the same person signs in as. Distinct from <paramref name="Id"/> and needed alongside
/// it: an identity provider authenticates a username and attributes a subject, so re-checking
/// a password at a signing gate needs the name they type, not the identifier we file them
/// under. Falls back to the subject where the provider issues no username.
/// </param>
public sealed record Subject(
    string Id,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Affiliates,
    IReadOnlyList<string> Markets,
    string Username = "")
{
    public string Username { get; } = string.IsNullOrWhiteSpace(Username) ? Id : Username;
}

/// <summary>Reads a <see cref="Subject"/> out of an authenticated principal (FN-IAM-001).</summary>
public static class SubjectFactory
{
    public const string RolesClaim = "roles";
    public const string AffiliatesClaim = "affiliates";
    public const string MarketsClaim = "markets";
    public const string UsernameClaim = "preferred_username";

    /// <summary>
    /// The subject the token describes, or null when the principal is not authenticated or
    /// carries no subject identifier.
    /// </summary>
    /// <remarks>
    /// A token with no scope claims yields a subject with empty scope rather than a
    /// permissive one: least privilege by default (CAP-IAM-007). An unscoped token must not
    /// mean unrestricted access.
    /// </remarks>
    public static Subject? From(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? principal.FindFirst("sub")?.Value;

        return string.IsNullOrWhiteSpace(id)
            ? null
            : new Subject(id, Values(principal, RolesClaim), Values(principal, AffiliatesClaim),
                Values(principal, MarketsClaim),
                principal.FindFirst(UsernameClaim)?.Value ?? id);
    }

    private static IReadOnlyList<string> Values(ClaimsPrincipal principal, string claim) =>
        [.. principal.FindAll(claim).Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v))];
}
