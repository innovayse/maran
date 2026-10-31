namespace Maran.Modules.Identity.Queries.ListAuditEvents;

/// <summary>
/// The public names the audit journal sorts by — the <c>sort</c> parameter's vocabulary, written once
/// and read by <see cref="ListAuditEventsSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class AuditEventSortFields
{
    /// <summary>When the action happened.</summary>
    public const string OccurredAt = "occurredAt";

    /// <summary>The name the actor used.</summary>
    public const string Actor = "actor";

    /// <summary>The machine-stable action the row records.</summary>
    public const string Action = "action";
}
