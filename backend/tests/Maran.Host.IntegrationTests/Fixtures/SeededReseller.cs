namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// One reseller the identity-side reseller tests stored: the reseller the accounts module owns and the
/// login the identity module created for it.
/// </summary>
/// <param name="ResellerId">The reseller's identity in the accounts module.</param>
/// <param name="UserId">The identity of the reseller's login.</param>
/// <param name="Username">The login's name, which is also the reseller's name.</param>
public sealed record SeededReseller(Guid ResellerId, Guid UserId, string Username);
