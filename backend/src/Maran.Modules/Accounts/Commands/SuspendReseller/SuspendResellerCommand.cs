namespace Maran.Modules.Accounts.Commands.SuspendReseller;

/// <summary>
/// Suspends a reseller: its login is refused and its mutations are refused. Its customers' accounts are
/// not touched; an administrator suspends those separately.
/// </summary>
/// <param name="ResellerId">The reseller to suspend.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record SuspendResellerCommand(Guid ResellerId, string IpAddress, string UserAgent);
