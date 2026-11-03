using FluentValidation;
using Maran.Modules.Accounts.Domain.Policies;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Validators;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Accounts.Commands.CreateReseller;

/// <summary>
/// Validates <see cref="CreateResellerCommand"/> before it reaches the handler (rules/security.md
/// "Input"). The name becomes a login name and the address becomes the recipient of mail, so both are
/// held to the panel's own rules for such values rather than to a looser, local definition.
/// </summary>
public sealed class CreateResellerCommandValidator : AbstractValidator<CreateResellerCommand>
{
    /// <summary>
    /// A storage bound, not a definition of validity: the reseller's address is held to the same width
    /// as an account owner's, which is narrower than <see cref="EmailAddressRule.MaximumLength"/>.
    /// </summary>
    private const int MaxContactEmailLength = 256;

    /// <summary>Configures the field rules for <see cref="CreateResellerCommand"/>.</summary>
    public CreateResellerCommandValidator()
    {
        // The code is the validation MESSAGE, not an error code: ExceptionMiddleware forwards only a
        // message that is entirely alphanumeric and resolves it as a resx key.
        RuleFor(command => command.Name)
            .Must(ResellerNamePolicy.IsAcceptable)
            .WithMessage(nameof(ErrorMessages.ResellerNameInvalid));

        // EmailAddressRule, not FluentValidation's .EmailAddress(), for the reason
        // CreateAccountCommandValidator states: the built-in accepts a display-name form and control
        // characters, and this value is handed to the mailer as a recipient (rules/security.md item 4).
        RuleFor(command => command.ContactEmail)
            .NotEmpty()
            .MaximumLength(MaxContactEmailLength)
            .Must(EmailAddressRule.IsAddress);

        RuleFor(command => command.Language)
            .Must(PanelLanguages.IsSupported)
            .WithMessage(nameof(ErrorMessages.AccountOwnerLanguageUnsupported));

        RuleFor(command => command.Envelope)
            .NotNull()
            .WithMessage(nameof(ErrorMessages.ResellerEnvelopeInvalid))
            .SetValidator(new ResellerEnvelopeDtoValidator());
    }
}
