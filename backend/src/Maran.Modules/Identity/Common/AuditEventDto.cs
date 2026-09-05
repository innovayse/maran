namespace Maran.Modules.Identity.Common;

/// <summary>Outward view of one audit journal row, for the administrator's audit screen.</summary>
/// <param name="Id">The event's identity.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="ActorUsername">The name the actor used.</param>
/// <param name="Action">What was attempted.</param>
/// <param name="Subject">What it was attempted on.</param>
/// <param name="IpAddress">Where it came from.</param>
/// <param name="Succeeded">Whether it worked.</param>
public sealed record AuditEventDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string ActorUsername,
    string Action,
    string Subject,
    string IpAddress,
    bool Succeeded);
