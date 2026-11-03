namespace Maran.Modules.Accounts.Models;

/// <summary>
/// What one reseller owns right now, measured from the rows: the figures an envelope is compared with
/// and a screen shows beside it. Passed between the usage reader and the mapper and never serialised.
/// </summary>
/// <param name="AccountCount">How many accounts, in any status, the reseller owns.</param>
/// <param name="PlanCount">How many plans the reseller owns.</param>
/// <param name="DiskAllocatedMb">The sum of the disk quotas, in megabytes, of the plans its accounts are on.</param>
public sealed record ResellerUsage(int AccountCount, int PlanCount, long DiskAllocatedMb)
{
    /// <summary>The usage of a reseller that owns nothing.</summary>
    public static ResellerUsage None { get; } = new(0, 0, 0);
}
