namespace Maran.Modules.Accounts.Common;

/// <summary>
/// What an account owns, set against its plan's limits, one row per resource kind a composed module
/// counts. A kind whose module is not part of this panel has no row, rather than a row reading zero.
/// </summary>
/// <param name="AccountId">The account summarised.</param>
/// <param name="Resources">One row per counted kind, in the order the kinds are declared.</param>
public sealed record AccountResourceSummaryDto(Guid AccountId, IReadOnlyList<AccountResourceUsageDto> Resources);
