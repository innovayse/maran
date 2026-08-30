namespace Maran.Modules.Identity.Controllers.Requests;

/// <summary>The body of a sign-in request.</summary>
/// <param name="Username">The login name.</param>
/// <param name="Password">The plaintext password. Never logged and never echoed back.</param>
public sealed record LoginRequest(string Username, string Password);
