using FluentValidation;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Resources;

namespace Maran.Modules.Mail.Commands.CreateMailAlias;

/// <summary>
/// Validates <see cref="CreateMailAliasCommand"/> before it reaches the handler (rules/security.md
/// "Input"). The agent re-validates every value, so this never substitutes for its boundary.
/// </summary>
/// <remarks>
/// The local part names a line in the MTA's maps, so it is validated against the mailbox grammar and never
/// escaped; a catch-all has no local part, and one that arrives with text is refused rather than ignored,
/// because the caller meant something the panel would silently not do. Each message is a bare resx code.
/// </remarks>
public sealed class CreateMailAliasCommandValidator : AbstractValidator<CreateMailAliasCommand>
{
    /// <summary>Configures the field rules for <see cref="CreateMailAliasCommand"/>.</summary>
    public CreateMailAliasCommandValidator()
    {
        RuleFor(command => command.MailDomainId)
            .NotEmpty();

        RuleFor(command => command.Kind)
            .IsInEnum();

        When(command =>
        {
            return command.Kind == MailAliasKind.CatchAll;
        }, () =>
        {
            RuleFor(command => command.LocalPart)
                .Must(string.IsNullOrEmpty)
                .WithMessage(nameof(ErrorMessages.MailboxLocalPartInvalid));
        });

        When(command =>
        {
            return command.Kind != MailAliasKind.CatchAll;
        }, () =>
        {
            RuleFor(command => command.LocalPart)
                .Must(localPart =>
                {
                    return MailboxAddressPolicy.IsValidLocalPart(MailboxAddressPolicy.Normalize(localPart));
                })
                .WithMessage(nameof(ErrorMessages.MailboxLocalPartInvalid));
        });

        RuleFor(command => command.Targets)
            .Custom((targets, context) =>
            {
                var normalised = MailAliasTargetPolicy.Normalise(targets);
                if (!normalised.IsSuccess)
                {
                    context.AddFailure(normalised.Error!.Code);
                }
            });
    }
}
