namespace Maran.Modules.Accounts.Commands.ReactivateReseller;

/// <summary>Lifts a reseller's suspension: its login may sign in again and its mutations are accepted.</summary>
/// <param name="ResellerId">The reseller to reactivate.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record ReactivateResellerCommand(Guid ResellerId, string IpAddress, string UserAgent);
