using FluentValidation;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Interfaces;
using Maran.Modules.Mail.Resources;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Mail.Validators;

/// <summary>
/// The rules every mail server settings input obeys, written once for the command that enables the
/// server and the one that re-states its settings (rules/security.md "Input"). Each rule is
/// re-validated inside the agent as well.
/// </summary>
/// <typeparam name="TInput">The command being validated.</typeparam>
/// <remarks>
/// <para>
/// The host name is written into the configuration of three daemons, all of them line-oriented, so
/// it is VALIDATED and never escaped: <see cref="HostNameRule"/> is anchored with <c>\z</c> and so
/// refuses a trailing newline. It must also pass <see cref="ReservedHostNameRule"/>, an address
/// literal or a special-use name being no name the server could present. Only the static reserved
/// set applies and not the panel's own name: the command is administrator-only, and an operator may
/// legitimately name the mail server after the panel's host.
/// </para>
/// <para>
/// The three spam thresholds must rise in the order they act, greylist then header then reject, each
/// within <c>0..MaxSpamScore</c>: equal or inverted values leave a stage that can never fire, or one
/// that rejects what the earlier stage was meant to only mark. Each message is a bare resx key.
/// </para>
/// </remarks>
public abstract class MailServerSettingsInputValidator<TInput> : AbstractValidator<TInput>
    where TInput : IMailServerSettingsInput
{
    /// <summary>Configures the field rules shared by every settings input.</summary>
    protected MailServerSettingsInputValidator()
    {
        RuleFor(input => input.Hostname)
            .NotEmpty()
            .MaximumLength(HostNameRule.MaximumLength)
            .WithMessage(nameof(ErrorMessages.MailServerHostnameTooLong))
            .Must(HostNameRule.IsHostName)
            .WithMessage(nameof(ErrorMessages.MailServerHostnameInvalidFormat))
            .Must(hostname =>
            {
                return !HostNameRule.IsHostName(hostname) || !ReservedHostNameRule.IsReserved(hostname);
            })
            .WithMessage(nameof(ErrorMessages.MailServerHostnameReserved));

        RuleFor(input => input.MaxMessageMib)
            .InclusiveBetween(1, MailDefaults.MaxMessageSizeMib)
            .WithMessage(nameof(ErrorMessages.MailMessageSizeOutOfRange));

        RuleFor(input => input)
            .Must(SpamScoresRiseInOrder)
            .WithMessage(nameof(ErrorMessages.MailSpamScoresInvalid));
    }

    /// <summary>Whether the three thresholds are in range and strictly increasing.</summary>
    /// <param name="input">The settings under judgement.</param>
    /// <returns>True when <c>0 &lt;= greylist &lt; header &lt; reject &lt;= MaxSpamScore</c>.</returns>
    private static bool SpamScoresRiseInOrder(TInput input)
    {
        return input.SpamGreylistScore >= 0
            && input.SpamGreylistScore < input.SpamAddHeaderScore
            && input.SpamAddHeaderScore < input.SpamRejectScore
            && input.SpamRejectScore <= MailDefaults.MaxSpamScore;
    }
}
