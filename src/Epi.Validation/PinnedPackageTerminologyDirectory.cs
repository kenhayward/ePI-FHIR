using Epi.ContentCore;

namespace Epi.Validation;

/// <summary>
/// A terminology directory over the pinned conformance packages (ADR-036 decision 5).
/// </summary>
/// <remarks>
/// <para>
/// The reference implementation, and what a deployment with no terminology server has. It is
/// where terminology already comes from - validation resolves codes from these packages, which
/// is what makes validation offline and reproducible (ADR-016) - so this reports what is
/// actually answering rather than inventing a source.
/// </para>
/// <para>
/// Recorded separately from the conformance packages in a pin even though it is the same source
/// today, and that is the point rather than an oversight: the packages say what validated the
/// structure and the bindings say what answered the codes. When a terminology server is adopted
/// they diverge, and a pin written now will still mean what it says.
/// </para>
/// <para>
/// Only the terminology packages, not every pinned package. A structural profile package is not
/// something a code came from, and listing it as one would make the record say something untrue
/// in a place whose whole value is that it does not.
/// </para>
/// </remarks>
public sealed class PinnedPackageTerminologyDirectory(ConformanceManifest manifest)
    : ITerminologyDirectory
{
    /// <summary>
    /// What marks a package as a source of codes rather than of structure.
    /// </summary>
    /// <remarks>
    /// A name test, which is a heuristic and is stated as one. It is right for the packages the
    /// platform pins today and it is not a rule anybody should build on: the moment a real
    /// terminology source is configured, that source names itself and this stops being asked.
    /// </remarks>
    private const string TerminologyPackagePrefix = "hl7.terminology";

    private readonly ConformanceManifest _manifest =
        manifest ?? throw new ArgumentNullException(nameof(manifest));

    public Task<IReadOnlyList<TerminologyBindingRef>> BindingsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TerminologyBindingRef>>(
        [
            .. _manifest.Packages
                .Where(package => package.Name.StartsWith(
                    TerminologyPackagePrefix, StringComparison.Ordinal))
                .Select(package => new TerminologyBindingRef(package.Name, package.Version))
                .OrderBy(binding => binding.System, StringComparer.Ordinal),
        ]);

    /// <summary>
    /// Not implemented, and returning null rather than throwing.
    /// </summary>
    /// <remarks>
    /// Looking a code up needs a terminology service over these packages, and which service is
    /// the open question ADR-036 was written around. Null means "this directory does not
    /// recognise it", which is what a caller has to handle anyway (ADR-036 decision 4) - so a
    /// caller written against this port today keeps working when a real one is configured.
    /// Throwing would make the difference visible to every caller and force them all to change.
    /// </remarks>
    public Task<ConceptDesignation?> LookupAsync(
        string system, string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return Task.FromResult<ConceptDesignation?>(null);
    }
}
