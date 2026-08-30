namespace Maran.Modules.Identity.Commands.BeginTotpEnrolment;

/// <summary>Starts a two-factor enrolment, without enabling anything.</summary>
/// <param name="UserId">The user enrolling, taken from their own token.</param>
public sealed record BeginTotpEnrolmentCommand(Guid UserId);
