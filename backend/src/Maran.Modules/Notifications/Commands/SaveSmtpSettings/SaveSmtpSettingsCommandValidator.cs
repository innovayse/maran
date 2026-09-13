using FluentValidation;
using Maran.Modules.Notifications.Resources;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Notifications.Commands.SaveSmtpSettings;

/// <summary>Refuses mail settings the panel could not use, or could be made to misuse.</summary>
/// <remarks>
/// <para>
/// <b>Every text field is checked for control characters, and that is the security rule here.</b> A
/// mail message separates its headers with CRLF, so a newline inside the sender's display name or
/// the sender's address does not corrupt a header — it invents the next one (rules/security.md item
/// 4). <see cref="MailHeaderTextRule"/> carries the argument in full.
/// </para>
/// <para>
/// <b>The password is bounded but otherwise unconstrained.</b> It is a credential for somebody
/// else's system: this panel has no business having an opinion about its shape, and a policy here
/// would only ever refuse a working one.
/// </para>
/// </remarks>
public sealed class SaveSmtpSettingsCommandValidator : AbstractValidator<SaveSmtpSettingsCommand>
{
    /// <summary>The longest a host name may be.</summary>
    private const int HostMaxLength = 255;

    /// <summary>The longest a display name may be.</summary>
    private const int DisplayNameMaxLength = 200;

    /// <summary>The longest a submission password may be before the panel stops accepting it.</summary>
    /// <remarks>
    /// Bounded because the encrypted column is bounded, and generously so: the ceiling exists to stop
    /// a megabyte of text reaching the cipher, not to express an opinion about passwords.
    /// </remarks>
    private const int PasswordMaxLength = 256;

    /// <summary>The lowest and highest TCP port a mail server can listen on.</summary>
    private const int MinimumPort = 1;

    /// <summary>The highest TCP port number.</summary>
    private const int MaximumPort = 65535;

    /// <summary>Declares the rules.</summary>
    public SaveSmtpSettingsCommandValidator()
    {
        RuleFor(command => command.Host)
            .NotEmpty()
            .MaximumLength(HostMaxLength)
            .Must(MailHeaderTextRule.IsHeaderSafe)
            .WithErrorCode(nameof(ErrorMessages.SmtpHostInvalid));

        RuleFor(command => command.Port)
            .InclusiveBetween(MinimumPort, MaximumPort)
            .WithErrorCode(nameof(ErrorMessages.SmtpPortInvalid));

        RuleFor(command => command.Security)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.SmtpSecurityInvalid));

        RuleFor(command => command.Username)
            .NotNull()
            .MaximumLength(EmailAddressRule.MaximumLength)
            .Must(MailHeaderTextRule.IsHeaderSafe)
            .WithErrorCode(nameof(ErrorMessages.SmtpUsernameInvalid));

        RuleFor(command => command.Password)
            .MaximumLength(PasswordMaxLength)
            .When(command =>
            {
                return command.Password is not null;
            })
            .WithErrorCode(nameof(ErrorMessages.SmtpPasswordInvalid));

        RuleFor(command => command.FromAddress)
            .Must(EmailAddressRule.IsAddress)
            .WithErrorCode(nameof(ErrorMessages.SmtpFromAddressInvalid));

        RuleFor(command => command.FromName)
            .NotNull()
            .MaximumLength(DisplayNameMaxLength)
            .Must(MailHeaderTextRule.IsHeaderSafe)
            .WithErrorCode(nameof(ErrorMessages.SmtpFromNameInvalid));

        RuleFor(command => command.AlertRecipient)
            .Must(EmailAddressRule.IsAddress)
            .WithErrorCode(nameof(ErrorMessages.SmtpAlertRecipientInvalid));
    }
}
