namespace Maran.Modules.Identity.Commands.LogoutEverywhere;

/// <summary>Ends every session of one user.</summary>
/// <param name="UserId">
/// Whose sessions to end. Taken from the caller's own token by the controller, never from the
/// request body — there is no way to spell "everyone else's devices".
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the journal.</param>
public sealed record LogoutEverywhereCommand(Guid UserId, string IpAddress, string UserAgent);
