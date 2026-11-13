using FluentValidation;
using Maran.Modules.Notifications.Options;
using Maran.Modules.Notifications.Resources;
using Maran.SharedKernel.Utilities.Mail;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.Extensions.Options;

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
/// <para>
/// <b>Port 25 of THIS machine is refused unless the deployment opts in.</b> "This machine" is loopback,
/// <c>localhost</c>, any address of an interface and the host's own (fully qualified) name
/// (<see cref="ThisMachineRule"/>); the opt-in is <see cref="SmtpLocalRelayOptions.AllowLocalPort25Relay"/>,
/// for a host whose port 25 is a relay Maran does not manage. The refusal is advice: the managed mail
/// server refuses the same connection by itself, and a name that only resolves here is not recognised.
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

    /// <summary>The port a mail server accepts mail from other servers on, which the panel's own mail server closes to every local client.</summary>
    private const int TransferPort = 25;

    /// <summary>Creates the validator.</summary>
    /// <param name="relay">The deployment switch for a relay of its own on port 25.</param>
    /// <param name="machine">Where the addresses and names of the machine the panel runs on are read, at every validation.</param>
    public SaveSmtpSettingsCommandValidator(IOptions<SmtpLocalRelayOptions> relay, IThisMachineFactsSource machine)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(machine);

        var allowLocalRelay = relay.Value.AllowLocalPort25Relay;

        RuleFor(command => command.Host)
            .NotEmpty()
            .MaximumLength(HostMaxLength)
            .Must(MailHeaderTextRule.IsHeaderSafe)
            .WithErrorCode(nameof(ErrorMessages.SmtpHostInvalid))
            .WithMessage(nameof(ErrorMessages.SmtpHostInvalid));

        RuleFor(command => command.Port)
            .InclusiveBetween(MinimumPort, MaximumPort)
            .WithErrorCode(nameof(ErrorMessages.SmtpPortInvalid))
            .WithMessage(nameof(ErrorMessages.SmtpPortInvalid));

        // The mail server on this machine refuses every local client on port 25: that port is where mail
        // arrives from other servers, and a process on the machine must not be able to hand it mail as if
        // it were one. The panel submits through the submission port with a login instead, like any
        // other client of that server. A host whose port 25 is a relay Maran does not manage opts in.
        RuleFor(command => command.Port)
            .Must((command, port) =>
            {
                return allowLocalRelay || port != TransferPort || !ThisMachineRule.IsThisMachine(command.Host, machine.Read());
            })
            .WithErrorCode(nameof(ErrorMessages.SmtpLocalPort25Refused))
            .WithMessage(nameof(ErrorMessages.SmtpLocalPort25Refused));

        // IsInEnum also refuses the value 0 a request gets by omitting the field, because the enum has
        // no member with that value: the choice of a protection mode, cleartext included, is explicit.
        RuleFor(command => command.Security)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.SmtpSecurityInvalid))
            .WithMessage(nameof(ErrorMessages.SmtpSecurityInvalid));

        RuleFor(command => command.Username)
            .NotNull()
            .MaximumLength(EmailAddressRule.MaximumLength)
            .Must(MailHeaderTextRule.IsHeaderSafe)
            .WithErrorCode(nameof(ErrorMessages.SmtpUsernameInvalid))
            .WithMessage(nameof(ErrorMessages.SmtpUsernameInvalid));

        RuleFor(command => command.Password)
            .MaximumLength(PasswordMaxLength)
            .When(command =>
            {
                return command.Password is not null;
            })
            .WithErrorCode(nameof(ErrorMessages.SmtpPasswordInvalid))
            .WithMessage(nameof(ErrorMessages.SmtpPasswordInvalid));

        RuleFor(command => command.FromAddress)
            .Must(EmailAddressRule.IsAddress)
            .WithErrorCode(nameof(ErrorMessages.SmtpFromAddressInvalid))
            .WithMessage(nameof(ErrorMessages.SmtpFromAddressInvalid));

        RuleFor(command => command.FromName)
            .NotNull()
            .MaximumLength(DisplayNameMaxLength)
            .Must(MailHeaderTextRule.IsHeaderSafe)
            .WithErrorCode(nameof(ErrorMessages.SmtpFromNameInvalid))
            .WithMessage(nameof(ErrorMessages.SmtpFromNameInvalid));
    }
}
