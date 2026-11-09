namespace Maran.Modules.Accounts.Common;

/// <summary>An account's disk use beside what its plan allows, both in bytes as the agent measures them.</summary>
/// <param name="UsedBytes">The bytes the account's files occupy right now, as the agent counted them.</param>
/// <param name="LimitBytes">The bytes the account's plan allows: its disk quota converted from megabytes.</param>
public sealed record AccountDiskUsageDto(ulong UsedBytes, ulong LimitBytes);
