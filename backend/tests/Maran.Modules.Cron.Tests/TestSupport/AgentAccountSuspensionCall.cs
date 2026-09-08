namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>One account-wide cron suspension the handler under test asked the agent for.</summary>
/// <remarks>
/// Recorded separately from <see cref="AgentSetEntryEnabledCall"/> because the two calls express two
/// different facts, and a double that folded them together could not tell a suspension that wrote to
/// the customer's own enablement flag from one that did not — which is the defect the second marker
/// exists to make impossible.
/// </remarks>
/// <param name="AccountUsername">System username of the account whose whole crontab was addressed.</param>
/// <param name="Suspended">Whether the call suppressed the account's entries or restored them.</param>
public sealed record AgentAccountSuspensionCall(string AccountUsername, bool Suspended);
