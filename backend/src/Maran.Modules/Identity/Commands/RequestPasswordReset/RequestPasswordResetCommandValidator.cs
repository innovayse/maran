using FluentValidation;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Identity.Commands.RequestPasswordReset;

/// <summary>Bounds the address of <see cref="RequestPasswordResetCommand"/> before anything is looked up.</summary>
/// <remarks>
/// <para>
/// Refusing a malformed address discloses nothing: it is a fact about the text the caller typed, not
/// about who holds an account. What the endpoint must never do is answer differently for a
/// well-formed address that exists and a well-formed address that does not, and that is the
/// handler's guarantee rather than this type's.
/// </para>
/// <para>
/// The rule is the panel's shared one (<see cref="EmailAddressRule"/>) rather than
/// FluentValidation's <c>.EmailAddress()</c>, which in its ASP.NET-compatible mode asks only for an
/// <c>@</c> with something either side — and would let a display-name form or a header-injecting
/// newline reach the address field of a message the panel is about to compose.
/// </para>
/// </remarks>
public sealed class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
{
    /// <summary>Configures the field rules for <see cref="RequestPasswordResetCommand"/>.</summary>
    public RequestPasswordResetCommandValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.PasswordResetEmailInvalid))
            .MaximumLength(EmailAddressRule.MaximumLength)
                .WithMessage(nameof(Resources.ErrorMessages.PasswordResetEmailInvalid))
            .Must(EmailAddressRule.IsAddress)
                .WithMessage(nameof(Resources.ErrorMessages.PasswordResetEmailInvalid));
    }
}
