namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>An account's disk usage as the agent measured it.</summary>
/// <param name="UsedBytes">Bytes currently occupied by the account's home directory tree.</param>
/// <param name="Quota">
/// What the agent can honestly say about this account's disk quota. See <see cref="AccountQuota"/>.
/// The wire's deprecated <c>quota_bytes</c> mirror is not projected: it is zero for two of the three
/// cases and so never distinguishes "no limit configured" from "this filesystem cannot hold a limit
/// at all", which is exactly the collapse <see cref="AccountQuota"/> exists to end.
/// </param>
public sealed record AccountUsageDto(ulong UsedBytes, AccountQuota Quota);
