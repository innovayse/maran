namespace Maran.Modules.Identity.Controllers.Requests;

/// <summary>The body of a request completing a two-factor enrolment.</summary>
/// <param name="Secret">The secret handed out by the enrolment step.</param>
/// <param name="Code">A code the user's app produced from it, proving the enrolment worked.</param>
public sealed record ConfirmTwoFactorRequest(string Secret, string Code);
