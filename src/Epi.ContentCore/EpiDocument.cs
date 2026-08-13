using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// An immutable version snapshot of a canonical ePI document (CAP-SCM-007, CAP-LCM-002).
/// </summary>
public sealed record EpiDocument(DocumentIdentity Identity, int Version, Bundle Bundle);
