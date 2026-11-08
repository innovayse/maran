using FluentValidation;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Resources;

namespace Maran.Modules.Mail.Commands.CreateMailbox;

/// <summary>
/// Validates <see cref="CreateMailboxCommand"/> before it reaches the handler (rules/security.md
/// "Input"). The agent re-validates every one of these; this is the first boundary, never the only one.
/// </summary>
/// <remarks>
/// Each message is a bare resx key: <c>ExceptionMiddleware</c> forwards a validation message only when
/// it is a code, and resolves it against the module's resources. The local part is normalized exactly as
/// the handler normalizes it before the shape is judged, so what is accepted here is what is stored.
/// </remarks>
public sealed class CreateMailboxCommandValidator : AbstractValidator<CreateMailboxCommand>
{
    /// <summary>Configures the field rules for <see cref="CreateMailboxCommand"/>.</summary>
    public CreateMailboxCommandValidator()
    {
        RuleFor(command => command.MailDomainId)
            .NotEmpty();

        RuleFor(command => command.LocalPart)
            .Must(localPart =>
            {
                return MailboxAddressPolicy.IsValidLocalPart(MailboxAddressPolicy.Normalize(localPart));
            })
            .WithMessage(nameof(ErrorMessages.MailboxLocalPartInvalid));

        RuleFor(command => command.Password)
            .Must(password =>
            {
                return MailPasswordPolicy.IsAcceptable(password);
            })
            .WithMessage(nameof(ErrorMessages.MailPasswordTooWeak));

        RuleFor(command => command.QuotaMib)
            .InclusiveBetween(1, MailDefaults.MaxMailboxQuotaMib)
            .WithMessage(nameof(ErrorMessages.MailboxQuotaOutOfRange));
    }
}
