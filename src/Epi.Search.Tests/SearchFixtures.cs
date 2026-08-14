using Epi.ContentCore;
using Hl7.Fhir.Model;

namespace Epi.Search.Tests;

/// <summary>Synthetic content for the search tests. No real product or personal data.</summary>
internal static class SearchFixtures
{
    public static readonly DocumentScope Uk = new("uk-affiliate", "GB");

    public static readonly DocumentScope Eu = new("eu-affiliate", "EU");

    public static EpiDocument Document(
        string identifier,
        int version,
        DocumentScope scope,
        string title = "SYNTHETIC TEST LABEL - Examplinum 10 mg tablets",
        string? language = "en-GB",
        string? product = "Examplinum 10 mg tablets",
        string narrative = "Synthetic test content. Examplinum is not a real medicine.")
    {
        var composition = new Composition
        {
            Title = title,
            Language = language,
            Type = new CodeableConcept(
                "http://example.org/fhir/CodeSystem/synthetic-epi-document-type", "package-leaflet"),
            Subject = product is null ? [] : [new ResourceReference { Display = product }],
            Section =
            [
                new Composition.SectionComponent
                {
                    Title = "1. What Examplinum is and what it is used for",
                    Text = new Narrative
                    {
                        Status = Narrative.NarrativeStatus.Generated,
                        Div = $"<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>{narrative}</p></div>",
                    },
                },
            ],
        };

        var bundle = ContentScope.Stamp(
            new Bundle
            {
                Type = Bundle.BundleType.Document,
                Entry = [new Bundle.EntryComponent
                {
                    FullUrl = "urn:uuid:0195f3a0-0000-7000-8000-000000000001",
                    Resource = composition,
                }],
            },
            scope);

        return new EpiDocument(
            new DocumentIdentity(IdentifierAuthority.Demonstration.DocumentSystem, identifier),
            version,
            bundle);
    }
}
