namespace Maran.Modules.Monitoring.Queries.ListAccountDiskUsage;

/// <summary>
/// The public names the accounts disk list sorts by — the <c>sort</c> parameter's vocabulary, written
/// once and read by <see cref="ListAccountDiskUsageSorts.Map"/>, the tests and anything else that names
/// a sort.
/// </summary>
/// <remarks>
/// Deliberately no <c>usedBytes</c>: the agent measures only the page being shown, so an order by usage
/// cannot be known without measuring every account (see <see cref="ListAccountDiskUsageQuery"/>).
/// </remarks>
public static class AccountDiskUsageSortFields
{
    /// <summary>The account's system user name, which is what the list shows first.</summary>
    public const string Username = "username";

    /// <summary>The plan's disk allowance, which the panel holds without asking the agent.</summary>
    public const string QuotaBytes = "quotaBytes";
}
