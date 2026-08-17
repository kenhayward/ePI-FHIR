using Epi.ContentCore;
using Epi.Lifecycle;

namespace Epi.Search;

/// <summary>What one rebuild of the search projection did (FN-SCH-004).</summary>
/// <param name="Projected">Versions written to the index.</param>
/// <param name="WithoutContent">
/// Registrations whose content was not there to project. Reported rather than swallowed: each
/// one is an inert registration (FN-LCM-008), and a rebuild is the second place that notices
/// them.
/// </param>
public sealed record SearchRebuildReport(int Projected, int WithoutContent);

/// <summary>
/// Rebuilds the search projection from the canonical stores (ADR-022 decision 6, ADR-044).
/// </summary>
/// <remarks>
/// <para>
/// The projection is derived and never a source of truth, which is a claim with an obligation
/// attached: whatever is in it must be reconstructible from what is. That obligation went
/// unmet, and the cost only became visible when the walkthrough started restarting the service
/// - the index is in memory, so a restart emptied it, and content still sitting in the FHIR
/// server became unfindable with nothing to bring it back.
/// </para>
/// <para>
/// The lifecycle store says which versions exist, because it is the store that knows: a version
/// is registered before its content is written (ADR-025), so nothing that reached the content
/// store is missing from it. The content store says what each version says. Nothing here reads
/// the index to decide what to do, so a rebuild does not depend on the state it is repairing.
/// </para>
/// <para>
/// It writes only to the projection. A rebuild that touched a canonical store would be a
/// projection deciding something, which is the one thing its being derived rules out.
/// </para>
/// </remarks>
public sealed class SearchProjectionRebuild(
    ILifecycleStore lifecycle,
    IContentStore content,
    ISearchProjection projection,
    IdentifierAuthority? authority = null)
{
    // Whose identifiers these are is configuration (ADR-017), not a constant. A rebuild that
    // assumed the demonstration authority would look up every document under a system its
    // deployment does not use and find nothing - a rebuild that silently did nothing.
    private readonly IdentifierAuthority _authority = authority ?? IdentifierAuthority.Demonstration;

    /// <summary>Projects every content version the lifecycle store knows about.</summary>
    public async Task<SearchRebuildReport> RunAsync(CancellationToken cancellationToken = default)
    {
        // Everything, rather than everything before now. This is the store's only enumeration
        // and it takes a cutoff because the inert-registration report needs a settle period
        // (FN-LCM-008); a rebuild does not. A registration made a moment ago is as much a thing
        // to project as one made last year, and asking for "before now" would silently drop
        // whatever arrived while the query was being built.
        var registrations = await lifecycle.RegistrationsBeforeAsync(
            DateTimeOffset.MaxValue, cancellationToken);

        var projected = 0;
        var withoutContent = 0;

        foreach (var registration in registrations)
        {
            // Labels only. A render template is registered with the same engine and is not a
            // label: it has no scope, so a search returning one would be returning a result no
            // permission decision could be made about (ADR-022 decision 3, ADR-042 decision 3).
            if (!string.Equals(
                    registration.Kind, RegisteredArtefact.Content, StringComparison.Ordinal))
            {
                continue;
            }

            var document = await content.GetAsync(
                Identity(registration.Version.DocumentIdentifier),
                registration.Version.Version,
                cancellationToken);

            if (document is null)
            {
                // An inert registration: the content write never landed. Nothing to project -
                // no title, no scope, no language - and a hit with no scope is worse than no
                // hit, because scope is what keeps a result away from somebody who may not
                // see it.
                withoutContent++;
                continue;
            }

            // The state it reached, not the state it started in. A version approved before the
            // restart has to come back approved, or a search for what is approved answers with
            // what was approved before somebody last restarted the service.
            //
            // Null is impossible by construction - the version came from this store's own
            // registrations, and registering sets a state - so it is raised rather than
            // defaulted. A default here would put every affected version into the index under a
            // state nobody moved it to, which is a wrong answer wearing the shape of a right one.
            var state = await lifecycle.CurrentStateAsync(registration.Version, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"The lifecycle store lists {registration.Version.DocumentIdentifier} "
                    + $"version {registration.Version.Version} as registered and has no state "
                    + "for it. The projection cannot be rebuilt from a store that disagrees "
                    + "with itself.");

            await projection.ProjectAsync(document, state, cancellationToken);

            projected++;
        }

        return new SearchRebuildReport(projected, withoutContent);
    }

    private DocumentIdentity Identity(string value) => new(_authority.DocumentSystem, value);
}
