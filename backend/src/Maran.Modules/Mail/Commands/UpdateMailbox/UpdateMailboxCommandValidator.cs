using FluentValidation;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Resources;

namespace Maran.Modules.Mail.Commands.UpdateMailbox;

/// <summary>
/// Validates <see cref="UpdateMailboxCommand"/> before it reaches the handler (rules/security.md
/// "Input"). The ceiling for the send limit that depends on the account's plan is the handler's, because
/// only it has resolved the account.
/// </summary>
public sealed class UpdateMailboxCommandValidator : AbstractValidator<UpdateMailboxCommand>
{
    /// <summary>Configures the field rules for <see cref="UpdateMailboxCommand"/>.</summary>
    public UpdateMailboxCommandValidator()
    {
        RuleFor(command => command.QuotaMib)
            .InclusiveBetween(1, MailDefaults.MaxMailboxQuotaMib)
            .WithMessage(nameof(ErrorMessages.MailboxQuotaOutOfRange));

        RuleFor(command => command.SendLimitPerHour)
            .InclusiveBetween(1, MailDefaults.MaxSendLimitPerHour)
            .WithMessage(nameof(ErrorMessages.MailboxSendLimitOutOfRange));
    }
}
