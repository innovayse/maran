using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Writes audit entries into <c>identity.AuditEvents</c>, stamping each with the panel's clock and
/// the request's correlation id so a journal row and the log lines around it can be read together.
/// </summary>
/// <remarks>
/// <para>
/// The type has no update or delete method, and neither does <see cref="AuditEvent"/>. That absence
/// is what "append-only" means here — enforced by the shape of the code rather than by a convention
/// someone has to remember (spec §10).
/// </para>
/// <para>
/// <b>Every text field is fitted to its column here, at the one place they all pass through.</b>
/// The entry's fields are caller-supplied text arriving from a dozen modules — a claimed email
/// address on an anonymous password-reset request, the malformed address a refused firewall ban was
/// asked for — and a value one character over its column makes PostgreSQL raise 22001 and refuse
/// the INSERT. What is lost then is the journal row itself, so the caller who chose the over-long
/// value is the caller whose action goes unrecorded, and the request they made answers 500 instead
/// of the refusal it had already decided on. Bounding each field at its own validator is necessary
/// and was not sufficient: it was missing in three places at once, and a new module gets it wrong
/// by default. Fitting here makes an unrecordable entry impossible to construct rather than
/// unlikely, and <see cref="ColumnText.Fit"/> counts what the column counts.
/// </para>
/// <para>
/// The user agent is NOT fitted here: it arrives already capped by
/// <c>UserAgentText.Capped</c> at the controller, which is where its policy — keep whole grapheme
/// clusters where the width allows — belongs. Two caps on one value would leave the tighter one
/// silently deciding, and the constant they share would stop meaning what it says.
/// </para>
/// </remarks>
public sealed class DatabaseAuditWriter : IAuditWriter
{
    /// <summary>The module's database context; Identity owns the journal's table.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The current request's correlation id, tying a row to the logs.</summary>
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    /// <summary>Creates the writer.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="correlationIdAccessor">The current request's correlation id.</param>
    public DatabaseAuditWriter(
        IdentityDbContext dbContext,
        IClock clock,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _dbContext = dbContext;
        _clock = clock;
        _correlationIdAccessor = correlationIdAccessor;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        var correlationId = _correlationIdAccessor.CorrelationId;

        _dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            _clock.UtcNow,
            entry.ActorUserId,
            ColumnText.Fit(entry.ActorUsername, AuditEvent.ActorUsernameMaxLength),
            ColumnText.Fit(entry.Action, AuditEvent.ActionMaxLength),
            ColumnText.Fit(entry.Subject, AuditEvent.SubjectMaxLength),
            ColumnText.Fit(entry.IpAddress, AuditEvent.IpAddressMaxLength),
            entry.UserAgent,
            entry.Succeeded,
            correlationId is null ? null : ColumnText.Fit(correlationId, AuditEvent.CorrelationIdMaxLength)));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
