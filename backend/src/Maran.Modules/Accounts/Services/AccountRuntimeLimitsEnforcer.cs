using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Mappers;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// The module's one way of putting a plan's <see cref="RuntimeLimits"/> on the host: asked when an
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

    /// <summary>Holds every named account's slice to <paramref name="limits"/>.</summary>
    /// <param name="accountNames">The accounts' system user names.</param>
    /// <param name="limits">The ceilings to apply.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><c>null</c> once every account holds the ceilings, or the agent's typed failure that stopped it.</returns>
    public async Task<Error?> ApplyAsync(
        IReadOnlyList<string> accountNames,
        RuntimeLimits limits,
        CancellationToken cancellationToken)
    {
        var sliceLimits = RuntimeLimitsMapper.ToAgent(limits);
        foreach (var accountName in accountNames)
        {
            var applied = await _agent.SetResourceLimitsAsync(accountName, sliceLimits, cancellationToken);
            if (!applied.IsSuccess)
            {
                return applied.Error!;
            }
        }

        return null;
    }
}
