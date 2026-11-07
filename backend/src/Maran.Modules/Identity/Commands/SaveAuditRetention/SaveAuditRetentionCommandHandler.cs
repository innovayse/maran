using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.SaveAuditRetention;

/// <summary>
/// Handles <see cref="SaveAuditRetentionCommand"/> by writing the singleton row and journalling the
/// change with the old and the new window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Insert-or-update against a fixed key</b>, for the reason the security policy does it: two
/// concurrent saves contend on one row, and the panel can never hold two answers to "how long is the
/// journal kept".
/// </para>
/// <para>
/// <b>The change takes effect at the next daily run, not now.</b> This handler deletes nothing; the
/// maintenance run reads the row once a day. That delay is deliberate (threat note, section 2): it
/// gives the other administrators up to a day to see an <see cref="IdentityAuditActions.AuditRetentionChanged"/>
/// entry before a shortened window destroys anything.
/// </para>
/// <para>
/// <b>The entry is written after the commit</b>, like the security policy's: the old window is read
/// before the row is replaced, so the subject states what actually changed.
/// </para>
/// </remarks>
public sealed class SaveAuditRetentionCommandHandler
{
    /// <summary>The module's database context, which owns the retention row.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Records the change; it decides how long every audit row survives.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Who is asking, for the journal entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="journal">Records the change.</param>
    /// <param name="currentUser">Who is asking.</param>
    /// <param name="clock">The panel's clock, which stamps the row.</param>
    public SaveAuditRetentionCommandHandler(
        IdentityDbContext dbContext,
        IdentityAuditJournal journal,
        ICurrentUser currentUser,
        IClock clock)
    {
        _dbContext = dbContext;
        _journal = journal;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>Saves how long the audit journal is kept.</summary>
    /// <param name="command">The validated window.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success. The value is the caller's own input; there is nothing to hand back.</returns>
    public async Task<Result> HandleAsync(SaveAuditRetentionCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var policy = await _dbContext.AuditRetentionPolicies
            .FirstOrDefaultAsync(row => row.Id == AuditRetentionPolicy.SingletonId, cancellationToken);

        var previousDays = policy is null ? AuditRetentionPolicy.DefaultRetentionDays : policy.RetentionDays;

        if (policy is null)
        {
            _dbContext.AuditRetentionPolicies.Add(new AuditRetentionPolicy(command.RetentionDays, now));
        }
        else
        {
            policy.Replace(command.RetentionDays, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordIdentifiedAsync(
            SignedInCallerPolicy.Require(_currentUser).UserId,
            IdentityAuditActions.AuditRetentionChanged,
            AuditRetentionSubjectText.Change(previousDays, command.RetentionDays),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result.Ok();
    }
}
