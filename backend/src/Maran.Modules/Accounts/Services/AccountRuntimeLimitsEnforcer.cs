using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Models;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// The module's one way of putting an account's runtime ceilings on the host: a plan's
/// <see cref="RuntimeLimits"/>, or the account's own where it states one. Asked when an
/// account is created (<c>CreateAccountCommandHandler</c>), when a plan's ceilings are edited
/// (<c>UpdatePlanCommandHandler</c>) and when one account moves to another plan
/// (<c>ChangeAccountPlanCommandHandler</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one service and not three copies.</b> All three write the same three ceilings onto the same
/// slice through the same agent call; three copies of the translation are three places for a unit or
/// a case to drift.
/// </para>
/// <para>
/// <b>No fit check, unlike the disk quota.</b> A lowered disk quota under files already written breaks
/// every later write and the panel cannot make room, so it is refused. A lowered runtime ceiling has a
/// bounded, self-correcting effect confined to the account: CPU is throttled, the next fork past the
/// process ceiling fails while the processes already running keep running, and memory above a lowered
/// ceiling is reclaimed from the account — and, where it cannot be, the kernel's OOM killer ends one of
/// the ACCOUNT's own processes, never another tenant's. That is the ceiling doing what it was bought
/// for, so an edit is pushed rather than refused; an administrator lowering a memory ceiling under a
/// running workload is told so on the plan form.
/// </para>
/// <para>
/// <b>What an account holds is its own override, else its plan's.</b> The process ceiling an account
/// states for itself (<see cref="AccountLimitOverrides"/>) replaces the plan's for that account, so
/// every push here is made per account from <see cref="AccountLimitTarget"/> and a plan change or edit
/// can never erase the choice from the host. The same service puts an account's open-files ceiling on
/// the host, the other per-account figure the agent keeps no record of.
/// </para>
/// <para>
/// <b>Agent first, row second</b>, the order every handler in this module uses: a failure part-way
/// leaves the plan row as it was and reports the agent's code, and repeating the edit converges,
/// because the agent replaces all three ceilings on every call.
/// </para>
/// </remarks>
public sealed class AccountRuntimeLimitsEnforcer
{
    /// <summary>The agent, which owns every account's systemd slice.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>Creates the service.</summary>
    /// <param name="agent">The agent, which owns every account's systemd slice.</param>
    public AccountRuntimeLimitsEnforcer(IAgentAccountsClient agent)
    {
        _agent = agent;
    }

    /// <summary>Holds every account's slice to its own ceilings, else <paramref name="planLimits"/>.</summary>
    /// <param name="accounts">The accounts, each with what it states in place of the plan's.</param>
    /// <param name="planLimits">The ceilings of the plan the accounts are on.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><c>null</c> once every account holds its ceilings, or the agent's typed failure that stopped it.</returns>
    public async Task<Error?> ApplyAsync(
        IReadOnlyList<AccountLimitTarget> accounts,
        RuntimeLimits planLimits,
        CancellationToken cancellationToken)
    {
        foreach (var account in accounts)
        {
            var sliceLimits = RuntimeLimitsMapper.ToAgent(account.Overrides.ApplyTo(planLimits));
            var applied = await _agent.SetResourceLimitsAsync(account.Name, sliceLimits, cancellationToken);
            if (!applied.IsSuccess)
            {
                return applied.Error!;
            }
        }

        return null;
    }

    /// <summary>
    /// Puts the account's open-files ceiling on the host when it states one. An account that follows
    /// its plan is left alone: a plan states no such ceiling, and a new account has no drop-in to
    /// remove.
    /// </summary>
    /// <param name="accountName">The account's system user name.</param>
    /// <param name="overrides">What the account states in place of its plan's.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><c>null</c> once the host holds the ceiling (or needed nothing), or the agent's typed failure.</returns>
    public async Task<Error?> ApplyOpenFilesAsync(
        string accountName,
        AccountLimitOverrides overrides,
        CancellationToken cancellationToken)
    {
        if (overrides.OpenFilesLimit is not { } openFiles)
        {
            return null;
        }

        var applied = await _agent.SetOpenFilesLimitAsync(accountName, (ulong)openFiles, cancellationToken);

        return applied.IsSuccess ? null : applied.Error!;
    }
}
