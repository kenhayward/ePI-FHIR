using System.Security.Claims;

namespace Epi.Iam;

/// <summary>
/// Who is asking, and what they are scoped to (capability 17). Built from the tokens the
/// enterprise identity provider issues; the platform never authenticates anyone itself.
/// </summary>
public sealed record Subject(
    string Id,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Affiliates,
    IReadOnlyList<string> Markets);

/// <summary>Reads a <see cref="Subject"/> out of an authenticated principal (FN-IAM-001).</summary>
public static class SubjectFactory
{
    public const string RolesClaim = "roles";
    public const string AffiliatesClaim = "affiliates";
    public const string MarketsClaim = "markets";

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
                Values(principal, MarketsClaim));
    }

    private static IReadOnlyList<string> Values(ClaimsPrincipal principal, string claim) =>
        [.. principal.FindAll(claim).Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v))];
}
