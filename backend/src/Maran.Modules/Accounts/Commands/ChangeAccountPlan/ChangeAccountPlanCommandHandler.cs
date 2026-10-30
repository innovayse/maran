using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
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
/// measurement and the push are <see cref="AccountDiskQuotaEnforcer"/>'s, shared with that edit. A
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
/// when both plans sell the same disk.
/// </para>
/// <para>
/// <b>The target plan can be deleted between the check and the save.</b> <c>Accounts.PlanId</c> is a
/// foreign key, so that save fails with <c>SqlState 23503</c> and nothing else, and is answered
/// <c>PlanNotFound</c> — journalled as a refusal, never a server error.
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

    /// <summary>Measures and applies the disk quota on the host.</summary>
    private readonly AccountDiskQuotaEnforcer _quotas;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Resolves a shipped plan's name in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="quotas">Measures and applies the disk quota on the host.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Resolves a shipped plan's name in the request's culture.</param>
    public ChangeAccountPlanCommandHandler(
        AccountsDbContext dbContext,
        AccountDiskQuotaEnforcer quotas,
        AccountAuditJournal journal,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _quotas = quotas;
        _journal = journal;
        _displayNames = displayNames;
    }

    /// <summary>Moves the account, or refuses the move whole.</summary>
    /// <param name="command">The validated move; see <see cref="ChangeAccountPlanCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The account as it now stands; <c>AccountNotFound</c>; <c>PlanNotFound</c>;
    /// <c>AccountPlanQuotaBelowUsage</c> when the account already uses more disk than the new plan
    /// allows; or the agent's own typed failure when usage could not be read or the quota not applied.
    /// </returns>
    public async Task<Result<AccountDetailDto>> HandleAsync(
        ChangeAccountPlanCommand command,
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
                $"{account.Name}: {current.DisplayNameKey} -> {command.PlanId:D}",
                Error.Of(nameof(ErrorMessages.PlanNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        if (target.Id == current.Id)
        {
            // Already there: nothing to measure, push or journal.
            return Result<AccountDetailDto>.Ok(AccountDetailMapper.ToDto(account, current, _displayNames));
        }

        var subject = $"{account.Name}: {current.DisplayNameKey} -> {target.DisplayNameKey}";

        var quotaFailure = await ReconcileDiskQuotaAsync(account, current, target, cancellationToken);
        if (quotaFailure is not null)
        {
            return await FailAsync(command, subject, quotaFailure, cancellationToken);
        }

        account.ChangePlan(target.Id);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // The ONE narrowing: only 23503 means the target plan was deleted after it was read.
            return await FailAsync(
                command, subject, Error.Of(nameof(ErrorMessages.PlanNotFound), ErrorType.NotFound), cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.AccountPlanChanged, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDetailDto>.Ok(AccountDetailMapper.ToDto(account, target, _displayNames));
    }

    /// <summary>
    /// Puts the target plan's disk quota on the host for this account, after proving a smaller one
    /// does not put the account over its limit. Does nothing when both plans sell the same disk.
    /// </summary>
    /// <param name="account">The account being moved.</param>
    /// <param name="current">The plan it is on now.</param>
    /// <param name="target">The plan it is moving to.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><c>null</c> when the host holds the target quota (or needed nothing); otherwise the failure.</returns>
    private async Task<Error?> ReconcileDiskQuotaAsync(
        Account account,
        Plan current,
        Plan target,
        CancellationToken cancellationToken)
    {
        if (target.DiskQuotaMb == current.DiskQuotaMb)
        {
            return null;
        }

        IReadOnlyList<string> accountNames = [account.Name];
        if (target.DiskQuotaMb < current.DiskQuotaMb)
        {
            var fits = await _quotas.AllFitAsync(accountNames, target.DiskQuotaMb, cancellationToken);
            if (!fits.IsSuccess)
            {
                return fits.Error!;
            }

            if (!fits.Value)
            {
                return Error.Of(nameof(ErrorMessages.AccountPlanQuotaBelowUsage), ErrorType.Conflict);
            }
        }

        return await _quotas.ApplyAsync(accountNames, target.DiskQuotaMb, cancellationToken);
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
            AuditActions.AccountPlanChanged, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDetailDto>.Fail(error);
    }
}
