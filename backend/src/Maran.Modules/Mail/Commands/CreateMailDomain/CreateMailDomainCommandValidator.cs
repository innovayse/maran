using FluentValidation;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Resources;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Mail.Commands.CreateMailDomain;

/// <summary>
/// Validates <see cref="CreateMailDomainCommand"/> before it reaches the handler (rules/security.md
/// "Input"). The agent re-validates the name on its own side.
/// </summary>
/// <remarks>
/// <para>
/// The name is judged in its lower-case spelling, because that is the spelling the handler stores
/// and sends to the host: <c>Example.COM</c> and <c>example.com</c> are one domain. The shape rule is
/// the panel's one host-name rule (two or more labels, no leading or trailing hyphen, nothing after
/// the last label), and it is applied together with the reserved-name rule so an address literal,
/// <c>localhost</c> or a name under a private suffix cannot become a mail domain.
/// </para>
/// <para>
/// Each message is a bare resx key, not an English sentence: <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric and resolves it as an error code.
/// </para>
/// </remarks>
public sealed class CreateMailDomainCommandValidator : AbstractValidator<CreateMailDomainCommand>
{
    /// <summary>Configures the field rules for <see cref="CreateMailDomainCommand"/>.</summary>
    public CreateMailDomainCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(nameof(ErrorMessages.MailDomainInvalid))
            .MaximumLength(MailDefaults.DomainMaxLength)
            .WithMessage(nameof(ErrorMessages.MailDomainTooLong))
            .Must(name =>
            {
                return HostNameRule.IsHostName(name.ToLowerInvariant());
            })
            .WithMessage(nameof(ErrorMessages.MailDomainInvalid))
            .Must(name =>
            {
                return !ReservedHostNameRule.IsReserved(name.ToLowerInvariant());
            })
            .WithMessage(nameof(ErrorMessages.MailDomainInvalid));
    }
}
