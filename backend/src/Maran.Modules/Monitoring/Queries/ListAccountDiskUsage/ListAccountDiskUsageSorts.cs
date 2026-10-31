using Maran.Sdk.Contracts;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Monitoring.Queries.ListAccountDiskUsage;

/// <summary>
/// The orders the accounts disk list accepts. <see cref="ListAccountDiskUsageQueryValidator"/> refuses
/// any other sort and <see cref="ListAccountDiskUsageQueryHandler"/> orders by exactly these, so the
/// whitelist is written once.
/// </summary>
/// <remarks>
/// Over <see cref="AccountSnapshot"/> rather than the wire row because the page is chosen from the
/// panel's accounts BEFORE the agent measures anything: only the chosen page is measured, so every
/// field here is one the panel holds on its own.
/// </remarks>
public static class ListAccountDiskUsageSorts
{
    /// <summary>
    /// <see cref="AccountDiskUsageSortFields.Username"/> and <see cref="AccountDiskUsageSortFields.QuotaBytes"/>,
    /// by user name ascending when none is named — the order the list had before it was paged — with
    /// ties broken by the user name compared ordinally, since a Linux user name is case-sensitive and
    /// two names differing only in case are two accounts.
    /// </summary>
    /// <remarks>
    /// <c>quotaBytes</c> orders by the plan's allowance in megabytes; the row reports it in bytes, and
    /// the conversion is a multiplication by a constant, so the two orders are the same order.
    /// </remarks>
    public static readonly SortMap<AccountSnapshot> Map = new SortMapBuilder<AccountSnapshot>()
        .By(AccountDiskUsageSortFields.Username, account => account.Username)
        .By(AccountDiskUsageSortFields.QuotaBytes, account => account.DiskQuotaMb)
        .DefaultTo(AccountDiskUsageSortFields.Username, SortDirection.Ascending)
        .TieBreaker(account => account.Username);
}
