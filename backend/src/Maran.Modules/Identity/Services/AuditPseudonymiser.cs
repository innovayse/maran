using System.Globalization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Replaces what identifies a deleted person or a deleted hosting account in the audit journal with a
/// pseudonym, so the journal keeps saying WHAT happened and WHEN without saying to WHOM.
/// </summary>
/// <remarks>
/// <para>
/// <b>The caller owns the transaction, and this type never commits one.</b>
/// <see cref="PseudonymiseUserAsync"/> and <see cref="PseudonymiseAccountAsync"/> save in batches of
/// <see cref="BatchSize"/> so that erasing a busy account does not hold its whole history in memory,
/// but a batch is only durable when the caller's transaction commits. The caller therefore opens the
/// transaction BEFORE calling, and calls these methods BEFORE it stages the removals it is making:
/// every save below writes everything the context has staged, so a removal staged earlier would be
/// flushed in the middle of the erasure. Only <see cref="PseudonymiseOrphanedActorsAsync"/> opens its
/// own transaction, because its caller is the maintenance job and has none.
/// </para>
/// <para>
/// <b>What each rule changes.</b> A person's rows are matched by the identity recorded on them: the
/// actor id, the impersonator id, and the subject login id. Never by text: a row that only carries a
/// typed name (a refused sign-in) is kept whoever's erasure names that text. Within a matched row, a
/// subject equal to the person's login name, address or id is replaced when the person acted in it. The column changes themselves are the entity's own methods
/// (<see cref="AuditEvent.ErasePerson"/>, <see cref="AuditEvent.EraseAccount"/>), which also destroy
/// the commitment salt that keeps the hash chain verifiable; this type decides WHICH rows, never what
/// a row becomes.
/// </para>
/// <para>
/// <b>The change tracker is not cleared, and that is deliberate.</b> Clearing it would drop the
/// removals the calling handler has staged. Instead every journal row the context tracks is detached
/// after each batch, which is the only thing that grows with the size of the erasure.
/// </para>
/// <para>
/// <b>Nothing here logs or throws an original identifier.</b> Logs carry counts, and the journal
/// summaries carry the pseudonym and a count; the login name, address and id of an erased person never
/// reach a message, a subject or an exception text (threat note section 4).
/// </para>
/// <para>
/// <b>The orphaned-actor sweep is the dangerous path, because its input is database state.</b> It
/// erases every actor whose id points at no login, so anything that makes the login table read as
/// empty or short would erase the journal's actors in one run, irreversibly. It therefore refuses
/// when the table has no row at all, refuses when more than <see cref="MaxOrphanActorsPerRun"/>
/// distinct actors would be erased, and writes a failed summary row in both cases rather than
/// guessing. The cap is a safeguard against a defect, not a limit on legitimate use: a journal with
/// more orphans than the cap is the situation an operator should look at before anything is erased.
/// </para>
/// </remarks>
public sealed class AuditPseudonymiser
{
    /// <summary>How many journal rows one batch reads, changes and saves.</summary>
    public const int BatchSize = 1000;

    /// <summary>The most distinct orphaned actors one sweep will erase; above it the sweep erases nothing.</summary>
    public const int MaxOrphanActorsPerRun = 200;

    /// <summary>Pre-compiled log delegate for a sweep that stopped because the login table is empty.</summary>
    private static readonly Action<ILogger, Exception?> LogSweepRefusedNoUsers =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(AuditPseudonymiser)),
            "The orphaned-actor sweep did not run: the login table has no row, so every actor would "
            + "have read as deleted. Nothing was erased.");

    /// <summary>Pre-compiled log delegate for a sweep that stopped because it found too many orphaned actors.</summary>
    private static readonly Action<ILogger, int, int, Exception?> LogSweepRefusedTooMany =
        LoggerMessage.Define<int, int>(
            LogLevel.Error,
            new EventId(2, nameof(AuditPseudonymiser)),
            "The orphaned-actor sweep did not run: {Actors} distinct actors point at no login, above the "
            + "cap of {Cap}. Nothing was erased.");

    /// <summary>The module's database context, which owns the journal's table.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Writes the sweep's summary row.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Where a refused sweep is reported; carries counts only.</summary>
    private readonly ILogger<AuditPseudonymiser> _logger;

    /// <summary>Creates the pseudonymiser.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock, stamped on every erased row.</param>
    /// <param name="journal">Writes the orphaned-actor sweep's summary row.</param>
    /// <param name="logger">Where a refused sweep is reported.</param>
    public AuditPseudonymiser(
        IdentityDbContext dbContext,
        IClock clock,
        IdentityAuditJournal journal,
        ILogger<AuditPseudonymiser> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Pseudonymises the journal rows of one login that is being deleted.</summary>
    /// <param name="userId">The login being deleted.</param>
    /// <param name="identifiers">
    /// The login name and e-mail address the person was known by. In a row the person acted in, a subject
    /// equal to any of them, or to the id itself, is replaced. Empty entries are ignored.
    /// </param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>The pseudonym that now stands in for the person, and how many rows were matched.</returns>
    /// <remarks>
    /// The caller's transaction commits it. Rows that match only as the impersonator keep the actor
    /// columns of the living person who was viewed; the entity decides that per row.
    /// </remarks>
    public async Task<AuditErasureOutcome> PseudonymiseUserAsync(
        Guid userId,
        IReadOnlyCollection<string> identifiers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identifiers);

        var pseudonymId = AuditPseudonym.NewId();
        var rows = await ErasePersonAsync(userId, identifiers, pseudonymId, cancellationToken);

        return new AuditErasureOutcome(AuditPseudonym.UserName(pseudonymId), rows);
    }

    /// <summary>Pseudonymises the journal rows that carry a hosting account that is being deleted.</summary>
    /// <param name="accountId">The account being deleted.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>The text that now stands in for the account, and how many rows were matched.</returns>
    /// <remarks>
    /// Only the account columns change (subject and account id); the actor columns of the living people
    /// who acted stay, because an administrator's accountability for what was done to an account is not
    /// the account owner's personal data. Run it AFTER the account's logins are pseudonymised, so a row
    /// that names a deleted login is handled by the person rule first. The caller's transaction commits it.
    /// </remarks>
    public async Task<AuditErasureOutcome> PseudonymiseAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var pseudonymId = AuditPseudonym.NewId();
        var pseudonym = AuditPseudonym.AccountSubject(pseudonymId);
        var matched = await _dbContext.AuditEvents
            .Where(auditEvent => auditEvent.AccountId == accountId)
            .Select(auditEvent => auditEvent.Id)
            .ToListAsync(cancellationToken);

        var now = _clock.UtcNow;
        foreach (var chunk in matched.Chunk(BatchSize))
        {
            var rows = await _dbContext.AuditEvents
                .Where(auditEvent => chunk.Contains(auditEvent.Id))
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                row.EraseAccount(now, pseudonymId, pseudonym);
                ForgetSalt(row);
            }

            await SaveBatchAsync(cancellationToken);
        }

        return new AuditErasureOutcome(pseudonym, matched.Count);
    }

    /// <summary>
    /// Pseudonymises the rows of every actor whose id points at no login: users deleted before erasure
    /// existed, and any deletion path that did not call this type.
    /// </summary>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>How many journal rows were erased; <c>0</c> when there was nothing to do or the sweep refused.</returns>
    /// <remarks>
    /// <para>
    /// Opens and commits its own transaction, so it must not be called inside one. An actor of
    /// <see cref="Guid.Empty"/>, a missing actor and a row already carrying a pseudonym name are never
    /// matched. One summary row is journalled whatever the outcome: erased counts on success, the reason
    /// on a refusal (recorded as failed).
    /// </para>
    /// <para>
    /// A row is recognised as already erased by its pseudonym name and not by its erasure time: an
    /// account's erasure also stamps the time, on rows whose actor is still the original, and judging by
    /// the time would leave those actors un-erased for ever.
    /// </para>
    /// </remarks>
    public async Task<int> PseudonymiseOrphanedActorsAsync(CancellationToken cancellationToken)
    {
        if (!await _dbContext.Users.AnyAsync(cancellationToken))
        {
            LogSweepRefusedNoUsers(_logger, null);
            await _journal.RecordSystemAsync(
                IdentityAuditActions.PersonalDataErased,
                "orphaned-actor sweep refused: the login table is empty",
                succeeded: false,
                cancellationToken);

            return 0;
        }

        var actors = await Orphans()
            .Select(auditEvent => auditEvent.ActorUserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (actors.Count == 0)
        {
            return 0;
        }

        if (actors.Count > MaxOrphanActorsPerRun)
        {
            LogSweepRefusedTooMany(_logger, actors.Count, MaxOrphanActorsPerRun, null);
            await _journal.RecordSystemAsync(
                IdentityAuditActions.PersonalDataErased,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"orphaned-actor sweep refused: {actors.Count} actors exceed the cap of {MaxOrphanActorsPerRun}"),
                succeeded: false,
                cancellationToken);

            return 0;
        }

        var erased = 0;
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            foreach (var actor in actors)
            {
                erased += await ErasePersonAsync(actor, [], AuditPseudonym.NewId(), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await _journal.RecordSystemAsync(
            IdentityAuditActions.PersonalDataErased,
            string.Create(CultureInfo.InvariantCulture, $"orphaned-actors actors={actors.Count} rows={erased}"),
            succeeded: true,
            cancellationToken);

        return erased;
    }

    /// <summary>The rows whose actor is neither absent, the empty id, a living login nor already a pseudonym.</summary>
    /// <returns>A query over those rows.</returns>
    private IQueryable<AuditEvent> Orphans()
    {
        return _dbContext.AuditEvents.Where(auditEvent =>
            auditEvent.ActorUserId != null
            && auditEvent.ActorUserId != Guid.Empty
            && !EF.Functions.Like(auditEvent.ActorUsername, AuditPseudonym.UserNamePrefix + "%")
            && !_dbContext.Users.Any(user => user.Id == auditEvent.ActorUserId));
    }

    /// <summary>Applies the person rule to every row that matches <paramref name="userId"/>, batch by batch.</summary>
    /// <param name="userId">The login being erased.</param>
    /// <param name="identifiers">The names and addresses a subject may equal; may be empty.</param>
    /// <param name="pseudonymId">The random id standing in for the person across all their rows.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>How many rows were matched.</returns>
    private async Task<int> ErasePersonAsync(
        Guid userId,
        IReadOnlyCollection<string> identifiers,
        Guid pseudonymId,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            userId.ToString("D", CultureInfo.InvariantCulture),
        };
        foreach (var identifier in identifiers)
        {
            if (!string.IsNullOrWhiteSpace(identifier))
            {
                names.Add(identifier);
            }
        }

        // The ids first and the rows in chunks after: the set to change is fixed before the first row
        // changes, so the loop ends by construction and cannot revisit a row it has just rewritten.
        var matched = await MatchPersonAsync(userId, cancellationToken);

        var now = _clock.UtcNow;
        var pseudonymName = AuditPseudonym.UserName(pseudonymId);
        foreach (var chunk in matched.Chunk(BatchSize))
        {
            var rows = await _dbContext.AuditEvents
                .Where(auditEvent => chunk.Contains(auditEvent.Id))
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                row.ErasePerson(userId, now, pseudonymId, pseudonymName, names);
                ForgetSalt(row);
            }

            await SaveBatchAsync(cancellationToken);
        }

        return matched.Count;
    }

    /// <summary>Finds the journal rows that carry <paramref name="userId"/> as actor, operator or subject login.</summary>
    /// <param name="userId">The login being erased.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The ids of the matching rows, fixed before any of them is changed.</returns>
    /// <remarks>
    /// Matched by recorded identity only. The subject TEXT is never a selector: it is whatever the caller
    /// typed, and a login's name is chosen by the account owner who invites it, so a text match would let
    /// an owner pick (by naming a sub-user like a sprayed username, then removing it) which other people's
    /// rows get their address and user agent wiped (issue #464).
    /// </remarks>
    private async Task<List<Guid>> MatchPersonAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.AuditEvents
            .Where(auditEvent => auditEvent.ActorUserId == userId
                || (auditEvent.Impersonator != null && auditEvent.Impersonator.OperatorId == userId)
                || auditEvent.SubjectUserId == userId)
            .Select(auditEvent => auditEvent.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Makes sure the save of an erased row writes a destroyed salt, whatever the sealer did meanwhile.</summary>
    /// <param name="row">A row an erasure method has just been applied to.</param>
    /// <remarks>
    /// The entity nulls the salt in memory, and EF writes only what changed from the value it LOADED. If
    /// the sealer sealed this row between the load and the save, the stored salt is no longer the loaded
    /// null, and an unmarked property would leave it in the table next to replaced columns: the salt
    /// that was meant to be destroyed, and a commitment that no longer reproduces. Marking the property
    /// makes the update write the null either way. A row the erasure did not touch is left alone.
    /// </remarks>
    private void ForgetSalt(AuditEvent row)
    {
        if (row.IsErased)
        {
            _dbContext.Entry(row).Property(auditEvent => auditEvent.CommitmentSalt).IsModified = true;
        }
    }

    /// <summary>Saves the batch and stops tracking the journal rows it held.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Resolves once the batch is saved and detached.</returns>
    private async Task SaveBatchAsync(CancellationToken cancellationToken)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Only the journal's own entries: the caller's staged removals and loaded rows must survive.
        var tracked = _dbContext.ChangeTracker.Entries()
            .Where(entry => { return entry.Entity is AuditEvent or AuditImpersonator; })
            .ToList();
        foreach (var entry in tracked)
        {
            entry.State = EntityState.Detached;
        }
    }
}
