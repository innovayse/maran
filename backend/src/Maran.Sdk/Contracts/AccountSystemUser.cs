namespace Maran.Sdk.Contracts;

/// <summary>
/// The one fact unattended work needs about an account: the system user name every agent operation
/// on that account is addressed by. Deliberately narrower than <see cref="AccountSnapshot"/> — no
/// plan, no allowances, no owner contact, no status — because the window that hands it out answers
/// without the caller's tenant scope and must give away as little as the question needs.
/// </summary>
/// <param name="Id">The account's identity: the caller's own input, echoed back.</param>
/// <param name="Username">
/// The account's Linux system user name, which addresses every agent operation on its files, vhosts
/// and pools.
/// </param>
public sealed record AccountSystemUser(Guid Id, string Username);
