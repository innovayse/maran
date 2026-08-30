namespace Maran.Modules.Identity.Commands.ConfirmTotpEnrolment;

/// <summary>Completes a two-factor enrolment by proving the secret works.</summary>
/// <param name="UserId">The user enrolling, taken from their own token.</param>
/// <param name="Secret">The secret handed out by the enrolment step.</param>
/// <param name="Code">A code the user's app produced from it.</param>
/// <param name="IpAddress">The caller's address, recorded in the journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the journal.</param>
public sealed record ConfirmTotpEnrolmentCommand(
    Guid UserId,
    string Secret,
    string Code,
    string IpAddress,
    string UserAgent);
