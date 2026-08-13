using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;

namespace Epi.Validation;

/// <summary>
/// Technical validation (capability 11): structural conformance against the pinned
/// definitions, and reference integrity within the document.
/// </summary>
public sealed class StructuralValidator(IAsyncResourceResolver resolver)
{
    private readonly IAsyncResourceResolver _resolver = resolver;

    public ValidationReport Validate(Bundle bundle) => throw new NotImplementedException();
}
