using System.Globalization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// Seeds journal rows for the erasure tests and reads them back as one searchable text, so a test can
/// ask whether an original identifier survives ANYWHERE in a row, not only in the column it expects.
/// </summary>
public static class ErasureJournal
{
    /// <summary>Builds one journal row; every argument but the actor and the subject has a fixed default.</summary>
    /// <param name="actorUserId">The actor's id, or <c>null</c> for a row with no verified actor.</param>
    /// <param name="actorName">The actor's name.</param>
    /// <param name="subject">What the row says it was done to.</param>
    /// <param name="ipAddress">The address column.</param>
    /// <param name="userAgent">The user-agent column.</param>
    /// <param name="impersonator">The operator viewing as the actor, or <c>null</c>.</param>
    /// <param name="accountId">The account the row carries, or <c>null</c>.</param>
    /// <param name="action">The action column.</param>
    /// <param name="subjectUserId">The verified login the row is about, or <c>null</c> for a row whose subject is only text.</param>
    /// <returns>An unsealed row stamped at <see cref="ErasureHandlers.Now"/>.</returns>
    public static AuditEvent Row(
        Guid? actorUserId,
        string actorName,
        string subject,
        string ipAddress = "203.0.113.7",
        string userAgent = "agent/1.0",
        AuditImpersonator? impersonator = null,
        Guid? accountId = null,
        string action = IdentityAuditActions.LoginSucceeded,
        Guid? subjectUserId = null)
    {
        return new AuditEvent(
            Guid.NewGuid(),
            ErasureHandlers.Now,
            actorUserId,
            actorName,
            action,
            subject,
            ipAddress,
            userAgent,
            succeeded: true,
            correlationId: null,
            impersonator,
            accountId,
            subjectUserId);
    }

    /// <summary>Reads every journal row from the store, as the next request would see it.</summary>
    /// <param name="context">The database to read.</param>
    /// <returns>The rows, untracked.</returns>
    public static async Task<List<AuditEvent>> AllAsync(IdentityDbContext context)
    {
        return await context.AuditEvents.AsNoTracking().ToListAsync();
    }

    /// <summary>Every column of a row that can name a person or an account, joined into one text.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The actor, subject, address, client, account and impersonator columns, in one string.</returns>
    public static string TextOf(AuditEvent row)
    {
        return string.Join(
            '\n',
            row.ActorUserId?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty,
            row.ActorUsername,
            row.Subject,
            row.IpAddress,
            row.UserAgent,
            row.AccountId?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty,
            row.Impersonator?.OperatorId.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty,
            row.Impersonator?.Username ?? string.Empty,
            row.SubjectUserId?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty);
    }

    /// <summary>Whether any needle appears, in any identifying column, of any row.</summary>
    /// <param name="rows">The rows to search.</param>
    /// <param name="needles">The original identifiers to look for.</param>
    /// <returns><c>true</c> when at least one needle is found, case-insensitively.</returns>
    /// <remarks>
    /// A search that finds nothing proves nothing until it has been shown to find something, so every
    /// test that asserts an identifier is gone first calls this on the seeded rows and asserts it is
    /// found: the probe's positive control (rules/testing.md).
    /// </remarks>
    public static bool Mentions(IEnumerable<AuditEvent> rows, params string[] needles)
    {
        return rows.Any(row =>
        {
            var text = TextOf(row);
            return needles.Any(needle => { return text.Contains(needle, StringComparison.OrdinalIgnoreCase); });
        });
    }
}
