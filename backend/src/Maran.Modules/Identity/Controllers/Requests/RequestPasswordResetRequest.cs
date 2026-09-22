namespace Maran.Modules.Identity.Controllers.Requests;

/// <summary>The body of a password-reset request.</summary>
/// <param name="Email">
/// The address to send the link to. It may belong to nobody; the endpoint answers the same either
/// way, so nothing here distinguishes a real address from a guess.
/// </param>
public sealed record RequestPasswordResetRequest(string Email);
