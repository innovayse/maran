using FluentValidation;
using Maran.Modules.Accounts.Domain.Policies;
using Maran.Modules.Accounts.Resources;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Accounts.Commands.UpdateReseller;

/// <summary>
/// Validates <see cref="UpdateResellerCommand"/> before it reaches the handler (rules/security.md
/// "Input"). The name becomes a login name and the address the recipient of mail, so both are held to
/// the panel's own rules for such values, exactly as creation holds them.
/// </summary>
public sealed class UpdateResellerCommandValidator : AbstractValidator<UpdateResellerCommand>
{
    /// <summary>
    /// A storage bound, not a definition of validity: the reseller's address is held to the same width
    /// as at creation, which is narrower than <see cref="EmailAddressRule.MaximumLength"/>.
    /// </summary>
    private const int MaxContactEmailLength = 256;

    /// <summary>Configures the field rules for <see cref="UpdateResellerCommand"/>.</summary>
    public UpdateResellerCommandValidator()
    {
        // The code is the validation MESSAGE, not an error code: ExceptionMiddleware forwards only a
        // message that is entirely alphanumeric and resolves it as a resx key.
        RuleFor(command => command.Name)
            .Must(ResellerNamePolicy.IsAcceptable)
            .WithMessage(nameof(ErrorMessages.ResellerNameInvalid));

        // EmailAddressRule, not FluentValidation's .EmailAddress(), for the reason
        // CreateResellerCommandValidator states (rules/security.md item 4).
        RuleFor(command => command.ContactEmail)
            .NotEmpty()
            .MaximumLength(MaxContactEmailLength)
            .Must(EmailAddressRule.IsAddress);

        RuleFor(command => command.Language)
            .Must(PanelLanguages.IsSupported)
            .WithMessage(nameof(ErrorMessages.AccountOwnerLanguageUnsupported));
    }
}
