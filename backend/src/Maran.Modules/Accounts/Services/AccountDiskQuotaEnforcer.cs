using Maran.Agent.Client.Interfaces;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// The module's one answer to "does a disk quota fit these accounts" and "put this quota on the
/// host": asked when a plan's quota is edited (<c>UpdatePlanCommandHandler</c>) and when one account
/// is moved to another plan (<c>ChangeAccountPlanCommandHandler</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one service and not two copies.</b> Both operations put a new figure under accounts that
/// already hold files, and both must refuse a figure an account does not fit under, for the reason
/// <c>UpdatePlanCommandHandler</c>'s remarks give at length: the quota is a hard kernel limit, so an
/// account put under one smaller than its files stops being able to write at all. Two copies of the
/// measurement are a way to tighten one and forget the other.
/// </para>
/// <para>
/// <b>A usage that cannot be read is a failure, never a fit.</b> The agent's own typed error is
/// returned, so a check that cannot look never passes. An account holding exactly the quota fits:
/// it is full, not over.
/// </para>
/// </remarks>
public sealed class AccountDiskQuotaEnforcer
{
    /// <summary>The agent, which owns the disk quota and the usage of every account on the host.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>Creates the service.</summary>
    /// <param name="agent">The agent, which owns every account's disk quota and usage.</param>
    public AccountDiskQuotaEnforcer(IAgentAccountsClient agent)
    {
        _agent = agent;
    }

    /// <summary>Measures every account and reports whether all of them fit under a quota.</summary>
    /// <param name="accountNames">The accounts' system user names.</param>
    /// <param name="quotaMb">The quota they would have to fit under, in megabytes.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// <c>true</c> when every account's usage is at or under the quota, <c>false</c> as soon as one is
    /// over it, or the agent's typed failure when a usage could not be read.
    /// </returns>
    public async Task<Result<bool>> AllFitAsync(
        IReadOnlyList<string> accountNames,
        int quotaMb,
        CancellationToken cancellationToken)
    {
        var quotaBytes = QuotaBytes(quotaMb);
        foreach (var accountName in accountNames)
        {
            var usage = await _agent.GetUsageAsync(accountName, cancellationToken);
            if (!usage.IsSuccess)
            {
                return Result<bool>.Fail(usage.Error!);
            }

            if (usage.Value.UsedBytes > quotaBytes)
            {
                return Result<bool>.Ok(false);
            }
        }

        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Writes a quota to every account on the host. The agent's quota call replaces rather than
    /// increments, so repeating a call that failed part-way converges.
    /// </summary>
    /// <param name="accountNames">The accounts' system user names.</param>
    /// <param name="quotaMb">The quota to apply, in megabytes.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><c>null</c> once every account holds the quota, or the agent's typed failure that stopped it.</returns>
    public async Task<Error?> ApplyAsync(
        IReadOnlyList<string> accountNames,
        int quotaMb,
        CancellationToken cancellationToken)
    {
        var quotaBytes = QuotaBytes(quotaMb);
        foreach (var accountName in accountNames)
        {
            var applied = await _agent.SetQuotaAsync(accountName, quotaBytes, cancellationToken);
            if (!applied.IsSuccess)
            {
                return applied.Error!;
            }
        }

        return null;
    }

    /// <summary>Converts a plan's disk allowance to the bytes the agent's quota calls use.</summary>
    /// <param name="quotaMb">The quota in megabytes.</param>
    /// <returns>The quota in bytes.</returns>
    private static ulong QuotaBytes(int quotaMb)
    {
        return (ulong)quotaMb * 1024UL * 1024UL;
    }
}
