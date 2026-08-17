using Epi.ContentCore;
using Epi.Lifecycle;

namespace Epi.Search;

/// <summary>
/// The write side of the search projection (ADR-022 decision 6).
/// </summary>
/// <remarks>
/// Derived, and never a source of truth: everything here is recoverable from the content store
/// and the lifecycle store, and losing the projection loses nothing but the time to rebuild it.
/// That is what makes it safe for search to hold a copy of anything at all - an index that owns
/// state nobody else has is a second system of record that no inspection has been told about.
/// </remarks>
public interface ISearchProjection
{
    /// <summary>Records a version's searchable metadata, at the state it starts in.</summary>
    /// <param name="at">
    /// When this happened. Results come back most-recently-touched first (ADR-045), so the
    /// moment is part of what is projected rather than something an index decides for itself:
    /// a rebuild replays what happened and must reproduce the order it happened in, which an
    /// index stamping its own clock could not.
    /// </param>
    Task ProjectAsync(
        EpiDocument document, string state, DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>Records that a version has moved state.</summary>
    /// <remarks>
    /// A version the projection has never seen is ignored rather than invented: the projection
    /// cannot make a searchable record out of a state change alone, and a hit with no title,
    /// scope or language would be a result nobody could act on and, worse, one with no scope to
    /// filter it by.
    /// </remarks>
    Task ProjectStateAsync(
        VersionRef version, string state, DateTimeOffset at,
        CancellationToken cancellationToken = default);
}
