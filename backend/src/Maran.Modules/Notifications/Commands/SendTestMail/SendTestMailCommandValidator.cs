using FluentValidation;
using Maran.Modules.Notifications.Resources;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Notifications.Commands.SendTestMail;

/// <summary>Refuses a test message whose destination is not one bare, header-safe address.</summary>
/// <remarks>
/// The same rule the settings' own address fields use, and deliberately the same one: a value that
/// would be refused as the panel's sender must not be accepted as a recipient, because both end up
/// in a header on the same message (rules/security.md item 4).
/// </remarks>
public sealed class SendTestMailCommandValidator : AbstractValidator<SendTestMailCommand>
{
    /// <summary>Declares the rule.</summary>
    public SendTestMailCommandValidator()
    {
        RuleFor(command => command.Recipient)
            .Must(EmailAddressRule.IsAddress)
            .WithErrorCode(nameof(ErrorMessages.MailRecipientInvalid));
    }
}
