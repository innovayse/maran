namespace Maran.Modules.Accounts.Commands.DeleteReseller;

/// <summary>Removes a reseller and, through an event, its login. Refused while it still owns accounts or plans.</summary>
/// <param name="ResellerId">The reseller to remove.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DeleteResellerCommand(Guid ResellerId, string IpAddress, string UserAgent);
