namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>How much disk one hosting account is using right now.</summary>
/// <param name="AccountUsername">System username of the account.</param>
/// <param name="UsedBytes">Bytes currently used under the account's home directory.</param>
/// <remarks>
/// Used bytes and nothing else. The wire carries a <c>quota_bytes</c> field, the agent writes 0 into
/// it, and this type has no member for it: a quota is the PANEL's own data — chosen when the account
/// is created and stored by the Accounts module — so the panel already holds the exact figure, and
/// asking the agent to re-derive one from the filesystem would introduce a second answer that can
/// disagree with the first. Carrying the zero would be worse still, since 0 reads as "no quota".
/// </remarks>
public sealed record AgentAccountDiskUsage(string AccountUsername, ulong UsedBytes);
