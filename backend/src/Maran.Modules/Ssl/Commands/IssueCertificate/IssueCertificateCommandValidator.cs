using FluentValidation;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Ssl.Commands.IssueCertificate;

/// <summary>
/// Validates <see cref="IssueCertificateCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Re-validated inside the agent as well: the API's validation never substitutes for the
/// agent's own boundary check (rules/architecture.md "Agent").
/// </summary>
/// <remarks>
/// <para>
/// The message is a bare resx key, not an English sentence. <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric and then resolves it as an error code
/// against the module's resources; an English sentence is silently discarded and the customer gets
/// the generic failure instead.
/// </para>
/// <para>
/// The domain is checked here even though the handler looks it up among the caller's sites: it
/// becomes part of a path the agent canonicalizes, part of a JSON identifier sent to a third party,
/// and part of a rendered vhost.
/// </para>
/// <para>
/// A second reason to check it here: a reserved name must never reach the certificate authority.
/// A well-formed name that is an address literal, in the loopback family, under a special-use
/// suffix (<see cref="ReservedHostNameRule"/>) or the panel's own name
/// (<see cref="IReservedHostDirectory"/>) is refused with its own message key, which names no rule
/// and never the panel's name. The reservation runs only on a name the shape rule accepted, so one
/// fault is reported once
/// (docs/superpowers/notes/2026-10-26-reserved-hostnames-threat-note.md).
/// </para>
/// </remarks>
public sealed class IssueCertificateCommandValidator : AbstractValidator<IssueCertificateCommand>
{
    /// <summary>Configures the field rules for <see cref="IssueCertificateCommand"/>.</summary>
    /// <param name="panelHosts">Answers whether a name is the panel's own.</param>
    public IssueCertificateCommandValidator(IReservedHostDirectory panelHosts)
    {
        RuleFor(command => command.Domain)
            .NotEmpty()
            .MaximumLength(HostNameRule.MaximumLength)
            .Must(HostNameRule.IsHostName)
            .WithMessage(nameof(ErrorMessages.CertificateDomainInvalidFormat))
            .Must(domain =>
            {
                return !HostNameRule.IsHostName(domain)
                    || !(ReservedHostNameRule.IsReserved(domain) || panelHosts.IsPanelHostName(domain));
            })
            .WithMessage(nameof(ErrorMessages.CertificateDomainReserved));
    }
}
