using FluentValidation;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Resources;

namespace Maran.Modules.Mail.Commands.ChangeMailboxPassword;

/// <summary>
/// Validates <see cref="ChangeMailboxPasswordCommand"/> before it reaches the handler (rules/security.md
/// "Input"): the new password must satisfy the same policy a mailbox is created under.
/// </summary>
public sealed class ChangeMailboxPasswordCommandValidator : AbstractValidator<ChangeMailboxPasswordCommand>
{
    /// <summary>Configures the field rules for <see cref="ChangeMailboxPasswordCommand"/>.</summary>
    public ChangeMailboxPasswordCommandValidator()
    {
        RuleFor(command => command.Password)
            .Must(password =>
            {
                return MailPasswordPolicy.IsAcceptable(password);
            })
            .WithMessage(nameof(ErrorMessages.MailPasswordTooWeak));
    }
}
