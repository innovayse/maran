namespace Maran.Modules.Identity.Controllers.Requests;

/// <summary>The body of a request creating the panel's first administrator.</summary>
/// <param name="Token">The token from the installer's one-time link.</param>
/// <param name="Username">The administrator's login name.</param>
/// <param name="Email">The administrator's contact address.</param>
/// <param name="Password">The chosen password. Never logged and never echoed back.</param>
public sealed record CompleteSetupRequest(string Token, string Username, string Email, string Password);
