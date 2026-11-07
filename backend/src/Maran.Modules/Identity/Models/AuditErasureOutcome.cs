namespace Maran.Modules.Identity.Models;

/// <summary>What one pseudonymisation did, as much as a caller may repeat.</summary>
/// <param name="Pseudonym">
/// The text that now stands in for the erased person or account (<c>erased-user-1a2b3c4d</c>,
/// <c>erased-account-1a2b3c4d</c>). Never the original id, name or address: the original is not kept
/// anywhere the outcome could carry it from.
/// </param>
/// <param name="Rows">How many journal rows the erasure matched.</param>
public sealed record AuditErasureOutcome(string Pseudonym, int Rows);
