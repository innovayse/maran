namespace Maran.Modules.Identity.Commands.DisableTotp;

/// <summary>Turns the second factor off.</summary>
/// <param name="UserId">The user, taken from their own token.</param>
/// <param name="Code">A current code or a recovery code, proving the factor is still in their hands.</param>
/// <param name="IpAddress">The caller's address, recorded in the journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the journal.</param>
public sealed record DisableTotpCommand(Guid UserId, string Code, string IpAddress, string UserAgent);
