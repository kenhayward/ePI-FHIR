namespace Epi.Templates;

/// <summary>
/// One section a template says a label of this kind has (CAP-TPL-002).
/// </summary>
/// <param name="Identifier">
/// Stable across template versions, so a section can be recognised as the same section after
/// the template changes. Not the section identifier assigned to instantiated content: that is
/// minted per document (ADR-015 decision 6), because two labels from one template are two
/// documents and their sections are not the same sections.
/// </param>
/// <param name="Mandatory">
/// Whether an author may leave it out. An optional section is still scaffolded, because an
/// author removing one is a decision and an author never seeing one is an accident.
/// </param>
public sealed record TemplateSection(
    string Identifier,
    string Code,
    string CodeSystem,
    string Title,
    bool Mandatory = true,
    string? Boilerplate = null,
    IReadOnlyList<TemplateSection>? Sections = null)
{
    public IReadOnlyList<TemplateSection> Sections { get; } = Sections ?? [];
}

/// <summary>The profile a template produces content against (ADR-021 decision 2).</summary>
/// <remarks>
/// A target, not a restatement. The implementation guide says what a conformant ePI is; the
/// template says which of its sections this label type uses and in what order. A template able
/// to contradict the profile would be a second definition of conformance, and it would lose at
/// the write gate.
/// </remarks>
public sealed record ProfileTarget(string Package, string Version);

/// <summary>
/// A versioned definition of the sections a kind of label has (ADR-021, CAP-TPL-001).
/// </summary>
public sealed record LabelTemplate(
    string Identifier,
    int Version,
    string Name,
    string LabelType,
    ProfileTarget Profile,
    IReadOnlyList<TemplateSection> Sections);

/// <summary>Raised when a template could not be read or is not usable.</summary>
public sealed class InvalidTemplateException(IReadOnlyList<string> problems)
    : Exception($"The template is not valid and was not loaded:{Environment.NewLine}  "
        + string.Join($"{Environment.NewLine}  ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
