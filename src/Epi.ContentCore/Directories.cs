namespace Epi.ContentCore;

/// <summary>
/// A product as master data knows it (CAP-MDM-001, ADR-036 decision 1).
/// </summary>
/// <remarks>
/// This platform's idea of a product, not a supplier's. Whatever answers - an internal MDM
/// system, a regulatory data warehouse, an IDMP service - is adapted to this on the way in,
/// which is the anti-corruption layer capability 24 asks for and the thing that makes the
/// source replaceable without touching anything that uses it.
/// </remarks>
public sealed record Product(
    string Identifier,
    string Name,
    string? MarketingAuthorisationHolder = null,
    IReadOnlyList<string>? Markets = null)
{
    public IReadOnlyList<string> Markets { get; } = Markets ?? [];
}

/// <summary>
/// What a product reference resolves to (CAP-MDM-001).
/// </summary>
/// <remarks>
/// Never blocks a write on being reachable (ADR-036 decision 4). A directory that cannot answer
/// returns null and the caller decides what that means; a write gate that failed because a
/// master-data system was restarting would make an external system's availability a
/// precondition of authoring.
/// </remarks>
public interface IProductDirectory
{
    /// <summary>The product this identifier names, or null if the directory has no such entry.</summary>
    Task<Product?> FindAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>Products whose name contains this text, for choosing one rather than typing it.</summary>
    Task<IReadOnlyList<Product>> SearchAsync(
        string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a coding means, and which version of which system said so (CAP-TRM-001).
/// </summary>
/// <param name="Binding">
/// The system and the version that answered. The version is what makes the answer checkable
/// later: "SNOMED CT said so" is not a fact an inspection can verify, and "the 2026-03-01
/// international release said so" is (ADR-036 decision 2).
/// </param>
public sealed record ConceptDesignation(
    string System,
    string Code,
    string Display,
    TerminologyBindingRef Binding);

/// <summary>Which version of which code system answered (ADR-036 decision 2).</summary>
/// <remarks>
/// The same shape the pinned validating context records, held here so that
/// <see cref="Epi.ContentCore"/> does not have to depend on the lifecycle project to describe an
/// answer. The lifecycle side converts.
/// </remarks>
public sealed record TerminologyBindingRef(string System, string? Version)
{
    public bool IsVersioned => !string.IsNullOrWhiteSpace(Version);
}

/// <summary>
/// What a coding means, from whatever terminology a deployment has (CAP-TRM-001).
/// </summary>
/// <remarks>
/// Deliberately narrow. It answers what a code means and which version said so, and it does not
/// expose subsumption, expression constraints or any other capability specific to one server -
/// a port shaped around one supplier's features is a port that only that supplier can fill.
/// <para>
/// Which server, and which source for which concept domain, is an open programme question
/// (ADR-036 context). This exists so the answer is a component behind this interface and a
/// configuration entry, rather than a redesign.
/// </para>
/// </remarks>
public interface ITerminologyDirectory
{
    /// <summary>What this code means, or null where the directory does not recognise it.</summary>
    Task<ConceptDesignation?> LookupAsync(
        string system, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// The bindings this directory would answer from now, for recording at approval.
    /// </summary>
    /// <remarks>
    /// Asked at approval rather than assembled from what happened to be looked up, so a version
    /// records the terminology in force rather than the subset its content happened to touch.
    /// </remarks>
    Task<IReadOnlyList<TerminologyBindingRef>> BindingsAsync(
        CancellationToken cancellationToken = default);
}
