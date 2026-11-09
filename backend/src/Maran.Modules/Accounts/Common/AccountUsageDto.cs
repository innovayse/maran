using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// What an integration reads about one account's use: its state, the plan it is sold from, the disk it
/// occupies, and its resource counts set against the plan's limits.
/// </summary>
/// <remarks>
/// Every count is a statement, never a bare number: a caller that is not an administrator is shown the
/// <c>unavailable</c> case for each kind, because the modules that own the resources show it no rows,
/// and a zero would read as "this account owns nothing". Only the disk figure is measured here.
/// </remarks>
/// <param name="AccountId">The account the figures are about.</param>
/// <param name="Status">The account's current lifecycle state.</param>
/// <param name="StatusName">The state as a reader reads it, localized for the request's culture.</param>
/// <param name="PlanId">The id of the plan bounding this account's resource limits.</param>
/// <param name="PlanDisplayName">The plan's name, already in the request's language.</param>
/// <param name="Disk">The disk the account occupies beside the disk its plan allows.</param>
/// <param name="Resources">One row per counted resource kind, each count beside the plan's limit.</param>
public sealed record AccountUsageDto(
    Guid AccountId,
    AccountStatus Status,
    string StatusName,
    Guid PlanId,
    string PlanDisplayName,
    AccountDiskUsageDto Disk,
    IReadOnlyList<AccountResourceUsageDto> Resources);
