using Epi.ContentCore;

namespace Epi.Lifecycle;

/// <summary>
/// A registration with no content behind it, and what it costs (FN-LCM-008).
/// </summary>
/// <param name="BlocksVersionNumber">
/// Whether the document exists at some other version. If it does, this registration has
/// reserved a version number nobody can now write: the store refuses a second registration of
/// the same version, so a retry fails at registration rather than reaching the content store,
/// and the next write has to skip the number. If the document does not exist at all, nothing is
/// waiting to reuse the identifier - it is minted per document, and the author starts again
/// with a new one. The two need different remedies, so the report distinguishes them.
/// </param>
public sealed record InertRegistration(
    VersionRef Version,
    string Author,
    DateTimeOffset RegisteredAt,
    bool BlocksVersionNumber);

/// <summary>What one run of the reconciliation found, and what it was looking for.</summary>
/// <remarks>
/// The settle period is carried in the result rather than assumed by the reader, because a
/// count of inert registrations means nothing without it: the same system reports differently
/// at fifteen minutes and at a day, and two reports are only comparable if both say which.
/// </remarks>
public sealed record ReconciliationReport(
    DateTimeOffset RanAt,
    TimeSpan SettlePeriod,
    IReadOnlyList<InertRegistration> Inert);

/// <summary>
/// Finds registrations that no content write ever followed (ADR-025, FN-LCM-008).
/// </summary>
/// <remarks>
/// <para>
/// ADR-025 chose which way round to fail. Content written before its registration would be
/// ungoverned content - readable through every read path, in a system whose whole claim is that
/// content is governed. Registration written first leaves a record referring to nothing: every
/// read returns not found and every transition refuses, because both are decided on the
/// content. That one is inert, and inert was the right choice.
/// </para>
/// <para>
/// Inert is not the same as harmless. Each one silently reserves a version number, and nothing
/// has ever looked for them. Harmless individually and invisible in aggregate is the wrong pair
/// of properties to leave together, which is the whole reason this exists.
/// </para>
/// <para>
/// It reports and changes nothing. The lifecycle record is append-only, so removing an inert
/// registration would mean destroying an audit record to tidy up a report - and the record is
/// evidence that somebody attempted a write, which is worth keeping whether or not the write
/// landed. What to do about one is a human decision with a paper trail.
/// </para>
/// </remarks>
public sealed class InertRegistrationReport
{
    private readonly ILifecycleStore _lifecycle;
    private readonly IContentStore _content;
    private readonly string _documentSystem;
    private readonly TimeProvider _clock;

    /// <param name="documentSystem">
    /// The identifier system a lifecycle record's document identifier belongs to. The lifecycle
    /// store holds the value alone, because state is recorded against a version rather than
    /// against an identity; asking the content store needs both halves.
    /// </param>
    public InertRegistrationReport(
        ILifecycleStore lifecycle,
        IContentStore content,
        string documentSystem,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentSystem);
        ArgumentNullException.ThrowIfNull(clock);

        _lifecycle = lifecycle;
        _content = content;
        _documentSystem = documentSystem;
        _clock = clock;
    }

    /// <summary>Runs the reconciliation, ignoring anything registered within the settle period.</summary>
    /// <param name="settlePeriod">
    /// How recent a registration has to be to be given the benefit of the doubt. A content
    /// write happens moments after its registration, so without one this reports every write in
    /// flight - and a report that flags writes about to succeed trains its reader to ignore it.
    /// </param>
    public async Task<ReconciliationReport> RunAsync(
        TimeSpan settlePeriod, CancellationToken cancellationToken = default)
    {
        if (settlePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settlePeriod),
                settlePeriod,
                "A settle period of zero or less reports every write in flight, and this report "
                + "has no way to tell those from the ones that failed.");
        }

        var ranAt = _clock.GetUtcNow();
        var registrations = await _lifecycle.RegistrationsBeforeAsync(
            ranAt - settlePeriod, cancellationToken);

        var inert = new List<InertRegistration>();

        foreach (var registration in registrations)
        {
            var identity = new DocumentIdentity(
                _documentSystem, registration.Version.DocumentIdentifier);

            var versions = await _content.VersionsAsync(identity, cancellationToken);

            if (versions.Contains(registration.Version.Version))
            {
                continue;
            }

            inert.Add(new InertRegistration(
                registration.Version,
                registration.Author,
                registration.RegisteredAt,

                // The document exists at some other version, so this number is spent.
                BlocksVersionNumber: versions.Count > 0));
        }

        return new ReconciliationReport(ranAt, settlePeriod, inert);
    }
}
