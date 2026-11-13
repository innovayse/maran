using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.Extensions.Localization;
using Npgsql;

namespace Maran.Modules.Accounts.Commands.TransferAccount;

/// <summary>
/// Handles <see cref="TransferAccountCommand"/>: hands one account to another reseller, or to the
/// administrator, with a plan of the new owner, after proving the new owner can carry it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Administrator only, by the route.</b> The action carries the <c>AdminOnly</c> policy on top of the
/// controller's, so a reseller is refused with 403 before this runs, and the handler reads the
/// administrator's unfiltered context. Nothing else dispatches the command.
/// </para>
/// <para>
/// <b>The plan is re-paired, never carried over.</b> An account and its plan always share one owner, and
/// a plan of the former owner is invisible to the new one, so the move names a plan of the new owner
/// (<c>PlanOwnerMismatch</c> otherwise, the refusal account creation gives for the same pairing). The
/// new plan is applied to the host as a plan change is: a smaller disk quota is refused when the files
/// do not fit (<c>AccountPlanQuotaBelowUsage</c>), and the quota and runtime ceilings are pushed before
/// the row (<see cref="AccountPlanHostSynchronizer"/>).
/// </para>
/// <para>
/// <b>Envelope accounting on both sides, without a counter to keep in step.</b> A reseller's account
/// count and allocated disk are read from the rows (<c>ResellerUsageReader</c>, the allocation guard),
/// so the former owner's usage falls and the new owner's rises in the one save that changes
/// <c>ResellerId</c>. The new owner is admitted exactly as account creation admits it
/// (<c>ResellerAccountLimitReached</c>, <c>ResellerDiskPoolExceeded</c>, counting the new plan's quota)
/// and its allocation revision is bumped in the same save, so a concurrent claim of the last slot loses
/// with a concurrency failure (<c>ResellerAllocationChanged</c>) and the host is put back on the
/// account's former figures. The former owner's revision is not bumped: its pool only shrinks, and a
/// concurrent admission that counted this account too is merely more cautious than it needed to be.
/// Moving to the administrator has no envelope to admit against.
/// </para>
/// <para>
/// <b>Sessions.</b> The former reseller is locked out by the COMMIT, not by this handler: every request of
/// a reseller's view-as session asks the account's current owner (Identity's
/// <c>ViewAsSessionVerifier</c>), so a session opened before the save, or racing it, is refused from the
/// instant the row changes. An earlier version revoked BEFORE the host push and the save, which left a
/// window in which the reseller still owned the account and could open a fresh session that then
/// outlived the move (issue #460). What is announced is therefore announced AFTER the save
/// (<see cref="AccountOwnerChanged"/>, through <see cref="AccountOwnerChangeAnnouncer"/>) and only ends
/// the rows the check has made useless; a subscriber's failure is logged and never undoes the move. The
/// customer's own logins keep their sessions — their tokens name the account and no reseller. An
/// administrator's view-as session stays, as an administrator sees every account.
/// </para>
/// <para>
/// <b>The PHP bounds follow the plan.</b> When the new plan bounds PHP differently, the move announces
/// <c>AccountPlanChanged</c> after the commit through <see cref="AccountPlanChangeAnnouncer"/>, the
/// same path a plan change takes, so the account's pools are cut to the bound it now lives under.
/// </para>
/// <para>
/// <b>The target can disappear between the check and the save.</b> Both <c>Accounts.PlanId</c> and
/// <c>Accounts.ResellerId</c> are foreign keys, so the save fails with <c>SqlState 23503</c> and the
/// constraint name says which one: <c>PlanNotFound</c> or <c>ResellerNotFound</c>, journalled as
/// refusals, never as server errors.
/// </para>
/// <para>
/// <b>Repeating it converges.</b> The account already owned by the named owner on the named plan is the
/// unchanged account, with nothing pushed or journalled; already owned by that owner on another plan is
/// <c>AccountOwnerUnchanged</c>, because a plan change is its own operation.
/// </para>
/// <para>
/// <b>The journal subject names the account and both owners</b> (<c>name: from -&gt; to</c>) and both
/// plans' stored identities, never a translation, because an entry that reads differently per reader's
/// language cannot be searched. The administrator is written <c>(administrator)</c>, which no reseller
/// name can spell.
/// </para>
/// </remarks>
public sealed class TransferAccountCommandHandler
{
    /// <summary>The constraint that fails when the plan was deleted after it was read.</summary>
    private const string PlanForeignKey = "FK_Accounts_Plans_PlanId";

    /// <summary>The constraint that fails when the reseller was deleted after it was read.</summary>
    private const string ResellerForeignKey = "FK_Accounts_Resellers_ResellerId";

    /// <summary>How the administrator is written in a journal subject; reseller names cannot contain parentheses.</summary>
    private const string AdministratorSubject = "(administrator)";

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Admits the account into the new owner's envelope and claims the owner's row.</summary>
    private readonly ResellerAllocationGuard _allocation;

    /// <summary>Names the owners in the journal subject and the answer.</summary>
    private readonly ResellerNameDirectory _resellerNames;

    /// <summary>Puts the new plan's figures on the host, and the former ones back.</summary>
    private readonly AccountPlanHostSynchronizer _host;

    /// <summary>Tells the other modules, after the save, that the account has a new owner.</summary>
    private readonly AccountOwnerChangeAnnouncer _ownerAnnouncer;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Resolves a shipped plan's name in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Tells the other modules, after the save, that the account's PHP bounds changed.</summary>
    private readonly AccountPlanChangeAnnouncer _announcer;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="allocation">Admits the account into the new owner's envelope and claims the owner's row.</param>
    /// <param name="resellerNames">Names the owners in the journal subject and the answer.</param>
    /// <param name="host">Puts the new plan's figures on the host, and the former ones back.</param>
    /// <param name="ownerAnnouncer">Tells the other modules, after the save, that the account has a new owner.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Resolves a shipped plan's name in the request's culture.</param>
    /// <param name="announcer">Tells the other modules, after the save, that the account's PHP bounds changed.</param>
    public TransferAccountCommandHandler(
        AccountsDbContext dbContext,
        ResellerAllocationGuard allocation,
        ResellerNameDirectory resellerNames,
        AccountPlanHostSynchronizer host,
        AccountOwnerChangeAnnouncer ownerAnnouncer,
        AccountAuditJournal journal,
        IStringLocalizer<DisplayNames> displayNames,
        AccountPlanChangeAnnouncer announcer)
    {
        _dbContext = dbContext;
        _allocation = allocation;
        _resellerNames = resellerNames;
        _host = host;
        _ownerAnnouncer = ownerAnnouncer;
        _journal = journal;
        _displayNames = displayNames;
        _announcer = announcer;
    }

    /// <summary>Moves the account, or refuses the move whole.</summary>
    /// <param name="command">The validated move; see <see cref="TransferAccountCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The account as it now stands; <c>AccountNotFound</c>; <c>PlanNotFound</c>; <c>ResellerNotFound</c>;
    /// <c>AccountOwnerUnchanged</c>; <c>PlanOwnerMismatch</c>; <c>ResellerAccountLimitReached</c>;
    /// <c>ResellerDiskPoolExceeded</c>; <c>AccountPlanQuotaBelowUsage</c> when the account already uses
    /// more disk than the new plan allows; <c>ResellerAllocationChanged</c> when the owner's allocation
    /// changed under the move; or the agent's own typed failure.
    /// </returns>
    public async Task<Result<AccountDetailDto>> HandleAsync(
        TransferAccountCommand command,
        CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts
            .SingleOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);
        if (account is null)
        {
            // Nothing exists to name, so the subject is the identifier the caller supplied.
            return await FailAsync(
                command,
                command.AccountId.ToString("D"),
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var current = await _dbContext.Plans.AsNoTracking().SingleAsync(p => p.Id == account.PlanId, cancellationToken);
        var target = await _dbContext.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.PlanId, cancellationToken);
        if (target is null)
        {
            return await FailAsync(
                command,
                $"{account.Name}: {command.PlanId:D}",
                Error.Of(nameof(ErrorMessages.PlanNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var names = await _resellerNames.NamesOfAsync(
            new[] { account.ResellerId, command.ResellerId }.OfType<Guid>().ToList(), cancellationToken);
        var from = OwnerSubject(account.ResellerId, names);
        var to = OwnerSubject(command.ResellerId, names);
        var subject = $"{account.Name}: {from} -> {to} (plan {current.DisplayNameKey} -> {target.DisplayNameKey})";

        if (account.ResellerId == command.ResellerId)
        {
            return target.Id == current.Id
                ? Result<AccountDetailDto>.Ok(ToDto(account, current, names))
                : await FailAsync(
                    command,
                    subject,
                    Error.Of(nameof(ErrorMessages.AccountOwnerUnchanged), ErrorType.Conflict),
                    cancellationToken);
        }

        var refusal = await _allocation.AdmitAccountAsync(command.ResellerId, target, cancellationToken)
            ?? ResellerAllocationGuard.RequirePlanOwnedBy(command.ResellerId, target);
        if (refusal is not null)
        {
            return await FailAsync(command, subject, refusal.Error, cancellationToken);
        }

        var hostFailure = await _host.ApplyAsync(account, current, target, cancellationToken);
        if (hostFailure is not null)
        {
            return await FailAsync(command, subject, hostFailure, cancellationToken);
        }

        account.TransferTo(command.ResellerId, target.Id);
        var saveRefusal = await SaveAsync(account, current, target, cancellationToken);
        if (saveRefusal is not null)
        {
            return await FailAsync(command, subject, saveRefusal, cancellationToken);
        }

        // After the commit: from here the ownership check refuses the former owner's view-as sessions,
        // and this ends their rows. Announced before anything that can fail to finish the move's answer.
        await _ownerAnnouncer.AnnounceAsync(account.Id, cancellationToken);

        if (current.PhpLimits != target.PhpLimits)
        {
            // After the commit, as a plan change announces it: the move re-pairs the account with a plan
            // of the new owner, and when that plan bounds PHP differently the account's pools carry
            // settings cut to the old bound. A subscriber's failure is logged, never failing the move.
            await _announcer.AnnounceAsync(
                [account.Id], current.PhpLimits.UploadMaxMb != target.PhpLimits.UploadMaxMb, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AccountAuditActions.AccountOwnerChanged,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken,
            account.Id);

        return Result<AccountDetailDto>.Ok(ToDto(account, target, names));
    }

    /// <summary>
    /// Writes the move, and puts the host back on the former plan's figures when the row could not be
    /// written. Best effort on the restore: the failure answered is the refused move, and a restore that
    /// fails too converges when the move is repeated, the agent's calls being replacements.
    /// </summary>
    /// <param name="account">The account that was moved.</param>
    /// <param name="current">The plan the row still says it is on when the save fails.</param>
    /// <param name="target">The plan whose figures were pushed ahead of the row.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><c>null</c> when the row was written; otherwise the typed refusal.</returns>
    private async Task<Error?> SaveAsync(
        Account account,
        Plan current,
        Plan target,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The new owner's row was claimed by another write between the admission and this save, so
            // the room the admission counted is no longer the room. Which limit it was is not known
            // without counting again, and a retry gets the exact refusal.
            await _host.RestoreAsync(account, current, target, cancellationToken);

            return Error.Of(nameof(ErrorMessages.ResellerAllocationChanged), ErrorType.Conflict);
        }
        catch (DbUpdateException exception)
            when (ForeignKeyViolated(exception) is PlanForeignKey or ResellerForeignKey)
        {
            // The ONE narrowing: only 23503 on these two constraints means the plan or the reseller was
            // deleted after it was read, and the constraint says which.
            await _host.RestoreAsync(account, current, target, cancellationToken);

            return ForeignKeyViolated(exception) == PlanForeignKey
                ? Error.Of(nameof(ErrorMessages.PlanNotFound), ErrorType.NotFound)
                : Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound);
        }
    }

    /// <summary>Names the foreign key a failed save violated.</summary>
    /// <param name="exception">The failed save.</param>
    /// <returns>The constraint name when the database refused with 23503; otherwise <c>null</c>.</returns>
    private static string? ForeignKeyViolated(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } violation
            ? violation.ConstraintName
            : null;
    }

    /// <summary>Writes an owner as the journal subject names it.</summary>
    /// <param name="resellerId">The owner, or <c>null</c> for the administrator.</param>
    /// <param name="names">The names of the resellers involved.</param>
    /// <returns>The reseller's name, its id when no name is known, or the administrator's marker.</returns>
    private static string OwnerSubject(Guid? resellerId, IReadOnlyDictionary<Guid, string> names)
    {
        if (resellerId is not { } id)
        {
            return AdministratorSubject;
        }

        // A reseller that does not exist has no name: the id the caller supplied is the subject.
        return names.TryGetValue(id, out var name) ? name : id.ToString("D");
    }

    /// <summary>Maps the account as it now stands, naming its owner.</summary>
    /// <param name="account">The account.</param>
    /// <param name="plan">The plan it is on.</param>
    /// <param name="names">The names of the resellers involved.</param>
    /// <returns>The detail the answer carries.</returns>
    private AccountDetailDto ToDto(Account account, Plan plan, IReadOnlyDictionary<Guid, string> names)
    {
        var ownerName = account.ResellerId is { } id ? names.GetValueOrDefault(id) : null;
        return AccountDetailMapper.ToDto(account, plan, _displayNames, ownerName);
    }

    /// <summary>Journals a refused move and returns it as the typed failure.</summary>
    /// <param name="command">The move that was refused.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<AccountDetailDto>> FailAsync(
        TransferAccountCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AccountAuditActions.AccountOwnerChanged, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDetailDto>.Fail(error);
    }
}
