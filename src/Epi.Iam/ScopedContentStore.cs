using Epi.ContentCore;
using Hl7.Fhir.Model;

namespace Epi.Iam;

/// <summary>
/// Enforces affiliate and market scope on every content operation (FN-IAM-004, CAP-IAM-007).
/// </summary>
/// <remarks>
/// A decorator, so scoping is on the data path by construction rather than by each caller
/// remembering to check. Out-of-scope content is <em>invisible</em> on read rather than
/// refused: CAP-SCH-004 requires that results never leak content outside a caller's scope, and
/// a refusal tells the caller the document exists.
/// </remarks>
public sealed class ScopedContentStore(
    IContentStore inner, IPolicyDecisionPoint policy, Subject subject) : IContentStore
{
    private readonly IContentStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IPolicyDecisionPoint _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    private readonly Subject _subject = subject ?? throw new ArgumentNullException(nameof(subject));

    public async Task<EpiDocument> CreateAsync(Bundle bundle, CancellationToken cancellationToken = default)
    {
        await AuthoriseAsync("author", bundle, cancellationToken);
        return await _inner.CreateAsync(bundle, cancellationToken);
    }

    public async Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default)
    {
        await AuthoriseAsync("author", bundle, cancellationToken);
        return await _inner.CreateVersionAsync(identity, bundle, cancellationToken);
    }

    public async Task<EpiDocument?> GetAsync(
        DocumentIdentity identity, int version, CancellationToken cancellationToken = default) =>
        await VisibleAsync(await _inner.GetAsync(identity, version, cancellationToken), cancellationToken);

    public async Task<EpiDocument?> GetLatestAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default) =>
        await VisibleAsync(await _inner.GetLatestAsync(identity, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<int>> VersionsAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default)
    {
        // Version numbers are metadata about a document the caller may not see at all, so the
        // list is empty rather than partial when the document is out of scope.
        var latest = await GetLatestAsync(identity, cancellationToken);
        return latest is null ? [] : await _inner.VersionsAsync(identity, cancellationToken);
    }

    private async Task<EpiDocument?> VisibleAsync(EpiDocument? document, CancellationToken cancellationToken)
    {
        if (document is null)
        {
            return null;
        }

        var decision = await DecideAsync("read", document.Bundle, cancellationToken);
        return decision.Allowed ? document : null;
    }

    private async Task AuthoriseAsync(string action, Bundle bundle, CancellationToken cancellationToken)
    {
        var decision = await DecideAsync(action, bundle, cancellationToken);
        if (!decision.Allowed)
        {
            throw new AccessDeniedException(action, decision.Reason);
        }
    }

    private Task<AuthorizationDecision> DecideAsync(
        string action, Bundle bundle, CancellationToken cancellationToken)
    {
        var scope = ContentScope.Of(bundle);
        if (scope is null)
        {
            // Content with no scope cannot be authorised. Allowing it would create a class of
            // document that every caller can reach, which is the opposite of multi-tenancy.
            return Task.FromResult(AuthorizationDecision.Deny(
                "The content carries no affiliate and market scope, so no decision can be made about it."));
        }

        return _policy.DecideAsync(
            new AuthorizationQuery(_subject, action, new ResourceScope(scope.Affiliate, scope.Market)),
            cancellationToken);
    }
}

/// <summary>Raised when a subject may not perform an action on content in that scope.</summary>
public sealed class AccessDeniedException(string action, string reason)
    : Exception($"Access denied for action '{action}': {reason}")
{
    public string Action { get; } = action;
}
