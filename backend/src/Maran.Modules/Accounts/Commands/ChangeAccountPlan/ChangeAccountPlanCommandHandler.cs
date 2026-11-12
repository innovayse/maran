using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Npgsql;

namespace Maran.Modules.Accounts.Commands.ChangeAccountPlan;

/// <summary>
/// Handles <see cref="ChangeAccountPlanCommand"/>: moves one account to another plan, after proving
/// it fits, and puts the new plan's disk quota on the host.
/// </summary>
/// <remarks>
/// <para>
/// <b>The plan rules are the ones a plan edit already enforces, applied to one account.</b> An
/// account reads every limit through its plan and keeps no copy of its own, so moving it is the same
/// kind of change as editing its plan — and refused for the same reason. A new plan whose disk quota
/// is below what the account already holds is REFUSED, whole (<c>AccountPlanQuotaBelowUsage</c>):
/// the quota is a hard kernel limit, and an account put under one smaller than its files stops being
/// able to write anything (<c>UpdatePlanCommandHandler</c>'s remarks give the full argument). The
/// measurement and the push are <see cref="AccountPlanHostSynchronizer"/>'s, shared with the transfer of an account to another owner. A
/// usage that cannot be read is refused with the agent's own failure, never assumed to fit.
/// </para>
/// <para>
/// <b>The count limits (sites, databases, logins, cron entries, workers) are not reconciled</b>, for
/// the reason the plan edit gives: they gate CREATION. An account already holding more sites than the
/// new plan sells keeps them all and is refused the next one; nothing existing is removed or broken.
/// </para>
/// <para>
/// <b>Agent first, row second</b>, the order every handler in this module uses. A quota pushed and a
/// save that then fails leaves the host holding a figure the account fits under — harmless — and
/// repeating the change converges, the agent's quota call being a replacement. Nothing is pushed
/// when both plans sell the same disk. The new plan's runtime ceilings (CPU, memory, processes) are
/// pushed after the quota and before the row, when they differ from the current plan's, and are not
/// refused for the reason <see cref="AccountRuntimeLimitsEnforcer"/> gives; all of it is
/// <see cref="AccountPlanHostSynchronizer"/>'s.
/// </para>
/// <para>
/// <b>The target plan can be deleted between the check and the save.</b> <c>Accounts.PlanId</c> is a
/// foreign key, so that save fails with <c>SqlState 23503</c> and nothing else, and is answered
/// <c>PlanNotFound</c> — journalled as a refusal, never a server error.
/// </para>
/// <para>
/// <b>A reseller's account moves only between its owner's own plans, and only while the pool holds.</b>
/// The target plan must have the account's owner (<c>PlanOwnerMismatch</c> otherwise — for an
/// administrator too, or the account would vanish from the reseller's list), and a larger disk quota
/// must fit the owner's disk pool (<c>ResellerDiskPoolExceeded</c>), both decided before anything is
/// pushed to the host. The owner's row is claimed in the same save as the account's new plan; a
/// concurrent claim loses there, and the host is put back on the account's current plan's figures.
/// </para>
/// <para>
/// <b>The journal subject is the account's name and both plans' stored identities</b>
/// (<c>name: old -&gt; new</c>) — the operator's text or a shipped plan's resource key, never a
/// translation, because an entry that reads differently per reader's language cannot be searched.
/// </para>
/// </remarks>
public sealed class ChangeAccountPlanCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Refuses a write by a suspended reseller.</summary>
    private readonly ResellerScope _scope;

    /// <summary>Holds the move to the owner's plan ownership and disk pool, and claims the owner's row.</summary>
    private readonly ResellerAllocationGuard _allocation;

    /// <summary>Names the owning reseller in the answer.</summary>
    private readonly ResellerNameDirectory _resellerNames;

    /// <summary>Measures and applies the new plan's disk quota and runtime ceilings on the host.</summary>
    private readonly AccountPlanHostSynchronizer _host;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Resolves a shipped plan's name in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Tells the other modules, after the save, that the account's PHP bounds changed.</summary>
    private readonly AccountPlanChangeAnnouncer _announcer;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="scope">Refuses a write by a suspended reseller.</param>
    /// <param name="allocation">Holds the move to the owner's plan ownership and disk pool, and claims the owner's row.</param>
    /// <param name="resellerNames">Names the owning reseller in the answer.</param>
    /// <param name="host">Measures and applies the new plan's disk quota and runtime ceilings on the host.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Resolves a shipped plan's name in the request's culture.</param>
    /// <param name="announcer">Tells the other modules, after the save, that the account's PHP bounds changed.</param>
    public ChangeAccountPlanCommandHandler(
        AccountsDbContext dbContext,
        ResellerScope scope,
        ResellerAllocationGuard allocation,
        ResellerNameDirectory resellerNames,
        AccountPlanHostSynchronizer host,
        AccountAuditJournal journal,
        IStringLocalizer<DisplayNames> displayNames,
        AccountPlanChangeAnnouncer announcer)
    {
        _dbContext = dbContext;
        _scope = scope;
        _allocation = allocation;
        _resellerNames = resellerNames;
        _host = host;
        _journal = journal;
        _displayNames = displayNames;
        _announcer = announcer;
    }

    /// <summary>Moves the account, or refuses the move whole.</summary>
    /// <param name="command">The validated move; see <see cref="ChangeAccountPlanCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The account as it now stands; <c>AccountNotFound</c>; <c>PlanNotFound</c>;
    /// <c>ResellerSuspended</c>; <c>PlanOwnerMismatch</c>; <c>ResellerDiskPoolExceeded</c>;
    /// <c>AccountPlanQuotaBelowUsage</c> when the account already uses more disk than the new plan
    /// allows; or the agent's own typed failure when usage could not be read or the quota not applied.
    /// </returns>
    public async Task<Result<AccountDetailDto>> HandleAsync(
        ChangeAccountPlanCommand command,
        CancellationToken cancellationToken)
    {
        var inactive = await _scope.RequireActiveAsync(cancellationToken);
        if (inactive is not null)
        {
            return await FailAsync(command, command.AccountId.ToString("D"), inactive, cancellationToken);
        }

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
                $"{account.Name}: {current.DisplayNameKey} -> {command.PlanId:D}",
                Error.Of(nameof(ErrorMessages.PlanNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var resellerName = await _resellerNames.NameOfAsync(account.ResellerId, cancellationToken);
        if (target.Id == current.Id)
        {
            // Already there: nothing to measure, push or journal.
            return Result<AccountDetailDto>.Ok(
                AccountDetailMapper.ToDto(account, current, _displayNames, resellerName));
        }

        var subject = $"{account.Name}: {current.DisplayNameKey} -> {target.DisplayNameKey}";

        var refusal = ResellerAllocationGuard.RequirePlanOwnedBy(account.ResellerId, target)
            ?? await _allocation.AdmitPlanChangeAsync(account.ResellerId, current, target, cancellationToken);
        if (refusal is not null)
        {
            return await FailAsync(command, subject, refusal.Error, cancellationToken);
        }

        var hostFailure = await _host.ApplyAsync(account, current, target, cancellationToken);
        if (hostFailure is not null)
        {
            return await FailAsync(command, subject, hostFailure, cancellationToken);
        }

        account.ChangePlan(target.Id);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The owner's row was claimed by another write between the admission above and this save,
            // so the pool the admission counted is no longer the pool. The host already holds the
            // target's figures and the row still says the current plan, which an account over its
            // sold disk must not be left in: the current plan's figures are put back.
            await _host.RestoreAsync(account, current, target, cancellationToken);

            return await FailAsync(
                command,
                subject,
                Error.Of(nameof(ErrorMessages.ResellerDiskPoolExceeded), ErrorType.Conflict),
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // The ONE narrowing: only 23503 means the target plan was deleted after it was read.
            return await FailAsync(
                command, subject, Error.Of(nameof(ErrorMessages.PlanNotFound), ErrorType.NotFound), cancellationToken);
        }

        if (current.PhpLimits != target.PhpLimits)
        {
            // After the commit, and only when the two plans bound PHP differently: the account's pools
            // carry the customer's settings clamped to the plan, so only then is there anything to
            // re-render. A subscriber's failure is logged and never fails the move that already happened.
            await _announcer.AnnounceAsync(
                [account.Id], current.PhpLimits.UploadMaxMb != target.PhpLimits.UploadMaxMb, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AccountAuditActions.AccountPlanChanged,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken,
            account.Id);

        return Result<AccountDetailDto>.Ok(AccountDetailMapper.ToDto(account, target, _displayNames, resellerName));
    }

    /// <summary>Journals a refused move and returns it as the typed failure.</summary>
    /// <param name="command">The move that was refused.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<AccountDetailDto>> FailAsync(
        ChangeAccountPlanCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AccountAuditActions.AccountPlanChanged, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDetailDto>.Fail(error);
    }
}
