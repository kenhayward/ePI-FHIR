using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;

namespace Epi.ContentCore;

/// <summary>
/// Reads and writes the canonical ePI document representation: a FHIR document
/// <c>Bundle</c> anchored by a <c>Composition</c> (CAP-SCM-001, CAP-SCM-010).
/// </summary>
public static class EpiBundleReader
{
    // Permissive parsing is wrong here. Content that only half-parses is content we would
    // store and later be unable to reproduce, which is the failure CAP-SCM-010 exists to
    // prevent.
    private static readonly FhirJsonDeserializer Deserializer = new(new DeserializerSettings());

    /// <summary>Reads canonical JSON into a document Bundle.</summary>
    /// <exception cref="InvalidEpiBundleException">
    /// If the content is not parseable, is not a Bundle, is not of type <c>document</c>, or is
    /// not anchored by a Composition. Every problem found is reported, not only the first.
    /// </exception>
    public static Bundle Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        Resource resource;
        try
        {
            resource = Deserializer.DeserializeResource(json);
        }
        catch (DeserializationFailedException error)
        {
            // Report the parser's findings, not its stack: these messages are shown to the
            // person who submitted the content.
            throw new InvalidEpiBundleException(
                [.. error.Exceptions.Select(e => e.Message).DefaultIfEmpty(error.Message)]);
        }
        catch (Exception error) when (error is System.Text.Json.JsonException
                                          or FormatException or ArgumentException)
        {
            throw new InvalidEpiBundleException([error.Message]);
        }

        if (resource is not Bundle bundle)
        {
            throw new InvalidEpiBundleException(
                [$"Expected a Bundle but the content is a {resource.TypeName}."]);
        }

        var problems = new List<string>();

        if (bundle.Type != Bundle.BundleType.Document)
        {
            problems.Add(
                $"An ePI must be a Bundle of type 'document' but this one is of type "
                + $"'{bundle.Type?.GetLiteral() ?? "(none)"}'.");
        }

        if (bundle.Entry.Count == 0)
        {
            problems.Add("A document Bundle must contain entries, anchored by a Composition.");
        }
        else if (bundle.Entry[0].Resource is not Composition)
        {
            problems.Add(
                "The first entry of a document Bundle must be the anchoring Composition but is "
                + $"a {bundle.Entry[0].Resource?.TypeName ?? "(nothing)"}.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidEpiBundleException(problems);
        }

        return bundle;
    }

    /// <summary>Writes a document Bundle back to canonical JSON.</summary>
    public static string Write(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return bundle.ToJson();
    }

    /// <summary>
    /// A deep copy, so that content crossing a boundary cannot be mutated behind the store's
    /// back. Immutability has to be a property of the code, not an instruction in a comment.
    /// </summary>
    internal static Bundle Copy(Bundle bundle) => (Bundle)bundle.DeepCopy();
}
