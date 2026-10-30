using FluentValidation;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Accounts.Commands.ChangeAccountOwnerEmail;

/// <summary>
/// Validates <see cref="ChangeAccountOwnerEmailCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// <see cref="EmailAddressRule"/>, not FluentValidation's <c>.EmailAddress()</c>, for the reason
/// <c>CreateAccountCommandValidator</c> gives: the address becomes a mail recipient, so a display-name
/// form or a control character must never get through (rules/security.md item 4).
/// </remarks>
public sealed class ChangeAccountOwnerEmailCommandValidator : AbstractValidator<ChangeAccountOwnerEmailCommand>
{
    /// <summary>
    /// A storage bound, not a definition of validity: the address is written to two columns, the
    /// account's <c>OwnerEmail</c> (<c>varchar(256)</c>) and the login's <c>Email</c>
    /// (<c>varchar(254)</c>), so the narrower of the two is the cap.
    /// </summary>
    private const int MaxOwnerEmailLength = 254;

    /// <summary>Configures the field rules.</summary>
    public ChangeAccountOwnerEmailCommandValidator()
    {
        RuleFor(command => command.OwnerEmail)
            .NotEmpty()
            .MaximumLength(MaxOwnerEmailLength)
            .Must(EmailAddressRule.IsAddress);
    }
}
