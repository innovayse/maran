using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Common;

/// <summary>One outcome the audit journal can be filtered by, as the filter's choice list offers it.</summary>
/// <param name="Outcome">
/// The outcome itself: the enum, so the panel's one camelCase enum converter spells it exactly as the
/// journal's <c>outcome</c> filter binds it, and the filter sends back what it was given.
/// </param>
/// <param name="OutcomeName">The outcome as an administrator reads it, resolved for the request's culture.</param>
public sealed record AuditOutcomeChoiceDto(AuditOutcome Outcome, string OutcomeName);
