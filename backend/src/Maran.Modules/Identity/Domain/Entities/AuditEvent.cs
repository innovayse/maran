namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// One row of the append-only audit journal: who did what, when, from where, and whether it worked
/// (spec §10). The type has no mutating method at all — that absence is the append-only guarantee,
/// enforced by the shape of the code rather than by a convention someone has to remember.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>The event's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Who did it; null when the actor could not be identified.</summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>The name the actor used.</summary>
    public string ActorUsername { get; private set; }

    /// <summary>What was attempted.</summary>
    public string Action { get; private set; }

    /// <summary>What it was attempted on.</summary>
    public string Subject { get; private set; }

    /// <summary>Where it came from.</summary>
    public string IpAddress { get; private set; }

    /// <summary>What client it came from.</summary>
    public string UserAgent { get; private set; }

    /// <summary>Whether it worked.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>The request's correlation id, tying this row to the logs.</summary>
    public string? CorrelationId { get; private set; }
    /// <summary>Records an event.</summary>
    /// <param name="id">The event's identity.</param>
    /// <param name="occurredAt">When it happened, taken from <see cref="IClock"/>.</param>
    /// <param name="actorUserId">Who did it; null when the actor could not be identified, as on a failed login.</param>
    /// <param name="actorUsername">The name the actor used, which exists even when the user does not.</param>
    /// <param name="action">What was attempted, from <c>AuditActions</c>.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="ipAddress">Where it came from.</param>
    /// <param name="userAgent">What client it came from.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <param name="correlationId">The request's correlation id, tying this row to the logs.</param>
    public AuditEvent(
        Guid id,
        DateTimeOffset occurredAt,
        Guid? actorUserId,
        string actorUsername,
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        bool succeeded,
        string? correlationId)
    {
        Id = id;
        OccurredAt = occurredAt;
        ActorUserId = actorUserId;
        ActorUsername = actorUsername;
        Action = action;
        Subject = subject;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        Succeeded = succeeded;
        CorrelationId = correlationId;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private AuditEvent()
    {
        ActorUsername = string.Empty;
        Action = string.Empty;
        Subject = string.Empty;
        IpAddress = string.Empty;
        UserAgent = string.Empty;
    }

}
