using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Resources;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Puts one account's host on another plan's figures, and puts it back: the disk quota and the runtime
/// ceilings that an account reads through its plan and the agent enforces.
/// </summary>
/// <remarks>
/// <para>
/// <b>One place for what two operations do the same way.</b> Moving an account to another plan
/// (<c>ChangeAccountPlanCommandHandler</c>) and handing it to another owner with a plan of that owner's
/// (<c>TransferAccountCommandHandler</c>) both end with the account on a plan it was not on, and both
/// owe the host the same: a smaller quota is refused when the account's files no longer fit, the new
/// quota and ceilings are pushed before the row is written (agent first, row second), and a row that
/// then fails to save gets the former figures back. Two copies of that sequence would drift, and the
/// drift would be a host holding a figure the panel does not.
/// </para>
/// <para>
/// <b>What is pushed is what the account holds, not the plan's figure alone.</b> An account may state
/// its own inode and process ceilings (<see cref="Domain.ValueObjects.AccountLimitOverrides"/>), and the agent keeps no
/// record of them: its quota call replaces the inode ceiling along with the blocks, and its slice call
/// replaces every ceiling. A push of the plan's figure by itself would therefore erase the account's
/// choice from the host, so each push carries <c>override ?? plan</c>.
/// </para>
/// <para>
/// <b>The count limits are not reconciled</b>, for the reason the plan edit gives: they gate CREATION,
/// and an account holding more than the new plan sells keeps everything it has.
/// </para>
/// </remarks>
public sealed class AccountPlanHostSynchronizer
{
    /// <summary>Measures and applies the disk quota on the host.</summary>
    private readonly AccountDiskQuotaEnforcer _quotas;

    /// <summary>Applies the runtime ceilings on the host.</summary>
    private readonly AccountRuntimeLimitsEnforcer _runtimeLimits;

    /// <summary>Creates the synchronizer.</summary>
    /// <param name="quotas">Measures and applies the disk quota on the host.</param>
    /// <param name="runtimeLimits">Applies the runtime ceilings on the host.</param>
    public AccountPlanHostSynchronizer(AccountDiskQuotaEnforcer quotas, AccountRuntimeLimitsEnforcer runtimeLimits)
    {
        _quotas = quotas;
        _runtimeLimits = runtimeLimits;
    }

    /// <summary>
    /// Puts the target plan's disk quota and runtime ceilings on the host for the account, after
    /// proving a smaller quota does not put the account over its limit. Does nothing for a figure the
    /// two plans share.
    /// </summary>
    /// <param name="account">The account being moved.</param>
    /// <param name="current">The plan it is on now.</param>
    /// <param name="target">The plan whose figures it is to carry.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// <c>null</c> when the host holds the target's figures (or needed nothing);
    /// <c>AccountPlanQuotaBelowUsage</c> when the account's files do not fit the smaller quota; or the
    /// agent's own typed failure when usage could not be read or a figure not applied.
    /// </returns>
    public async Task<Error?> ApplyAsync(
        Account account,
        Plan current,
        Plan target,
        CancellationToken cancellationToken)
    {
        var quotaFailure = await ReconcileDiskQuotaAsync(account, current, target, cancellationToken);
        if (quotaFailure is not null)
        {
            return quotaFailure;
        }

        // Pushed only when what the account holds differs: the slice already holds the current plan's
        // ceilings, and a process ceiling the account states for itself hides a plan difference in it.
        if (account.LimitOverrides.ApplyTo(target.RuntimeLimits) != account.LimitOverrides.ApplyTo(current.RuntimeLimits))
        {
            return await _runtimeLimits.ApplyAsync([Target(account)], target.RuntimeLimits, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Puts the host back on the current plan's disk quota and runtime ceilings after a move whose row
    /// could not be written. Best effort: the failure the caller answers with is the refused move, and a
    /// restore that fails too converges when the move is repeated or the plan edited, the agent's
    /// calls being replacements.
    /// </summary>
    /// <param name="account">The account that was being moved.</param>
    /// <param name="current">The plan the row still says it is on.</param>
    /// <param name="target">The plan whose figures were pushed ahead of the row.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes once the restore was attempted.</returns>
    public async Task RestoreAsync(Account account, Plan current, Plan target, CancellationToken cancellationToken)
    {
        IReadOnlyList<AccountLimitTarget> accounts = [Target(account)];
        if (target.DiskQuotaMb != current.DiskQuotaMb)
        {
            await _quotas.ApplyAsync(accounts, current.DiskQuotaMb, cancellationToken);
        }

        if (account.LimitOverrides.ApplyTo(target.RuntimeLimits) != account.LimitOverrides.ApplyTo(current.RuntimeLimits))
        {
            await _runtimeLimits.ApplyAsync(accounts, current.RuntimeLimits, cancellationToken);
        }
    }

    /// <summary>States an account with what it holds in place of its plan's, as the enforcers take it.</summary>
    /// <param name="account">The account being moved.</param>
    /// <returns>The account's name and its overrides.</returns>
    private static AccountLimitTarget Target(Account account)
    {
        return new AccountLimitTarget(account.Name, account.LimitOverrides);
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

        return await _quotas.ApplyAsync([Target(account)], target.DiskQuotaMb, cancellationToken);
    }
}
