namespace Maran.Modules.Identity.Controllers.Requests;

/// <summary>The body of a request turning the second factor off.</summary>
/// <param name="Code">A current code or a recovery code, proving the factor is still in the user's hands.</param>
public sealed record DisableTwoFactorRequest(string Code);
