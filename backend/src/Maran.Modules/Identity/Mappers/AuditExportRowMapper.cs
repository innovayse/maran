using System.Globalization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;

namespace Maran.Modules.Identity.Mappers;

/// <summary>
/// Builds an <see cref="AuditExportRow"/> from an <see cref="AuditEvent"/>. One place, because the
/// audit export and the personal-data export write the same row, and two copies of one projection are
/// two chances for a column to be forgotten in one of them.
/// </summary>
public static class AuditExportRowMapper
{
    /// <summary>The instant's text: ISO-8601 in UTC, to the microsecond the database stores.</summary>
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    /// <summary>Projects one journal row into its export view.</summary>
    /// <param name="auditEvent">The row to project.</param>
    /// <returns>The row as an export file carries it.</returns>
    public static AuditExportRow From(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var outcome = auditEvent.Succeeded ? AuditOutcome.Succeeded : AuditOutcome.Failed;

        return new AuditExportRow(
            auditEvent.Id,
            auditEvent.OccurredAt.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture),
            auditEvent.ActorUserId,
            auditEvent.ActorUsername,
            auditEvent.Impersonator?.Username,
            auditEvent.Action,
            outcome.ToString().ToLowerInvariant(),
            auditEvent.Subject,
            auditEvent.IpAddress,
            auditEvent.UserAgent,
            auditEvent.AccountId,
            auditEvent.CorrelationId,
            auditEvent.ChainIndex,
            auditEvent.RowHash is { } hash ? Convert.ToHexStringLower(hash) : null);
    }
}
