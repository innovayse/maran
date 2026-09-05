namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>An account's disk usage as the agent measured it.</summary>
/// <param name="UsedBytes">Bytes currently occupied by the account's home directory tree.</param>
/// <param name="QuotaBytes">The quota in force, in bytes; zero means no quota is set.</param>
public sealed record AccountUsageDto(ulong UsedBytes, ulong QuotaBytes);
