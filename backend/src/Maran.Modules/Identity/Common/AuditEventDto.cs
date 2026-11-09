namespace Maran.Modules.Identity.Common;

/// <summary>Outward view of one audit journal row, for the audit screens.</summary>
/// <remarks>The administrator's reads the whole journal; the reseller's reads its own part of it, in this same shape.</remarks>
/// <param name="Id">The event's identity.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="ActorUsername">The name the actor used.</param>
/// <param name="Action">
/// What was attempted, as the machine-stable name the row records. Kept on the wire beside
/// <paramref name="ActionName"/>: it is what an administrator greps a log by and quotes in a
/// ticket, and it reads the same in every language.
/// </param>
/// <param name="ActionName">
/// The action as an operator reads it, resolved for the request's culture by
/// <c>AuditActionDisplayNames</c>. Falls back to the machine name for an action this build carries
/// no entry for, such as one a marketplace module recorded.
/// </param>
/// <param name="Subject">What it was attempted on.</param>
/// <param name="IpAddress">Where it came from.</param>
/// <param name="Succeeded">Whether it worked.</param>
/// <param name="OutcomeName">
/// Whether it worked, in words, localized for the request's culture by
/// <c>AuditOutcomeDisplayNames</c>: what the outcome badge prints, so the SPA holds no copy of it.
/// </param>
/// <param name="ImpersonatorUsername">
/// The operator who was viewing the panel as the actor when it happened; null when the actor acted
/// as themselves (issue #317).
/// </param>
public sealed record AuditEventDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string ActorUsername,
    string Action,
    string ActionName,
    string Subject,
    string IpAddress,
    bool Succeeded,
    string OutcomeName,
    string? ImpersonatorUsername);
