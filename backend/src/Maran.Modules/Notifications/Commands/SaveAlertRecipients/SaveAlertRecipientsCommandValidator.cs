using FluentValidation;
using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Resources;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Notifications.Commands.SaveAlertRecipients;

/// <summary>Refuses an alert recipient list that is too long, holds a bad address or names one twice.</summary>
/// <remarks>
/// Each address is checked with the panel's one definition of a mail address
/// (<see cref="EmailAddressRule"/>), which also refuses control characters, so an address cannot
/// carry a header of its own into the mail the alert becomes (rules/security.md item 4). Duplicates
/// are compared case-insensitively because the list is stored lower-cased.
/// </remarks>
public sealed class SaveAlertRecipientsCommandValidator : AbstractValidator<SaveAlertRecipientsCommand>
{
    /// <summary>Declares the rules.</summary>
    public SaveAlertRecipientsCommandValidator()
    {
        RuleFor(command => command.Addresses)
            .NotNull()
            .Must(addresses =>
            {
                return addresses is null || addresses.Count <= AlertRecipient.MaxCount;
            })
            .WithErrorCode(nameof(ErrorMessages.AlertRecipientsTooMany))
            .WithMessage(nameof(ErrorMessages.AlertRecipientsTooMany));

        RuleForEach(command => command.Addresses)
            .Must(EmailAddressRule.IsAddress)
            .WithErrorCode(nameof(ErrorMessages.AlertRecipientInvalid))
            .WithMessage(nameof(ErrorMessages.AlertRecipientInvalid));

        RuleFor(command => command.Addresses)
            .Must(addresses =>
            {
                return addresses is null || addresses
                    .Select(address => { return address?.Trim().ToLowerInvariant(); })
                    .Distinct()
                    .Count() == addresses.Count;
            })
            .WithErrorCode(nameof(ErrorMessages.AlertRecipientDuplicate))
            .WithMessage(nameof(ErrorMessages.AlertRecipientDuplicate));
    }
}
