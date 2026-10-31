using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;

namespace Maran.Modules.Identity.Mappers;

/// <summary>
/// Builds an <see cref="AuditEventDto"/> from an <see cref="AuditEvent"/>. One place, because two
/// readers project the same row — the paged journal and the landing page's recent entries — and two
/// copies of one projection are two chances for a column to be forgotten in one of them.
/// </summary>
public static class AuditEventMapper
{
    /// <summary>Projects one journal row into its outward view.</summary>
    /// <param name="auditEvent">The row to project.</param>
    /// <param name="actionName">
    /// The row's action as an operator reads it, already resolved for the request's culture by
    /// <c>AuditActionDisplayNames</c>. Passed in because a mapper translates and never decides, and a
    /// resource lookup is a decision about culture.
    /// </param>
    /// <returns>The entry as the audit screens see it.</returns>
    public static AuditEventDto From(AuditEvent auditEvent, string actionName)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        return new AuditEventDto(
            auditEvent.Id,
            auditEvent.OccurredAt,
            auditEvent.ActorUsername,
            auditEvent.Action,
            actionName,
            auditEvent.Subject,
            auditEvent.IpAddress,
            auditEvent.Succeeded);
    }
}
