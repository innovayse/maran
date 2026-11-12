using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
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
/// <b>The account an entry is about is stamped here too.</b> An account the entry carries itself
/// (<see cref="AuditEntry.AccountId"/>, passed by a journal that is certain of it) wins; otherwise a
/// customer session supplies the account it acts for, and a viewed-as customer is a customer session,
/// so those entries are stamped as well. An administrator, a reseller, an API key, an account-less
/// customer and an anonymous caller supply none: whom they act on is the subject's business, not the
/// session's, so the column stays empty rather than guessing.
/// </para>
/// <para>
/// The user agent is NOT fitted here: it arrives already capped by
/// <c>UserAgentText.Capped</c> at the controller, which is where its policy — keep whole grapheme
/// clusters where the width allows — belongs. Two caps on one value would leave the tighter one
/// silently deciding, and the constant they share would stop meaning what it says.
/// </para>
/// </remarks>
public sealed class DatabaseAuditWriter : IAuditWriter, IStagedAuditWriter
{
    /// <summary>The module's database context; Identity owns the journal's table.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The current request's correlation id, tying a row to the logs.</summary>
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    /// <summary>
    /// The current request's principal, read for TWO things only: the operator viewing as the actor,
    /// if any (issue #317), and the hosting account a customer acts for. Every module's journal reaches
    /// the table through this writer, so stamping them here is what puts the impersonator and the
    /// account on every entry written by a customer session, including entries from modules that have
    /// never heard of view-as or of the account filter.
    /// </summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the writer.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="correlationIdAccessor">The current request's correlation id.</param>
    /// <param name="currentUser">
    /// The current request's principal, read for the viewing operator and the customer's account only.
    /// </param>
    public DatabaseAuditWriter(
        IdentityDbContext dbContext,
        IClock clock,
        ICorrelationIdAccessor correlationIdAccessor,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _clock = clock;
        _correlationIdAccessor = correlationIdAccessor;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        Stage(entry);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Stage(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var correlationId = _correlationIdAccessor.CorrelationId;
        var impersonator = _currentUser.Caller is PrincipalCaller { ViewedBy: { } viewer }
            ? new AuditImpersonator(viewer.UserId, ColumnText.Fit(viewer.Username, AuditEvent.ActorUsernameMaxLength))
            : null;
        var accountId = entry.AccountId ?? (_currentUser.Caller as CustomerCaller)?.AccountId;

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
            correlationId is null ? null : ColumnText.Fit(correlationId, AuditEvent.CorrelationIdMaxLength),
            impersonator,
            accountId,
            entry.SubjectUserId));
    }
}
