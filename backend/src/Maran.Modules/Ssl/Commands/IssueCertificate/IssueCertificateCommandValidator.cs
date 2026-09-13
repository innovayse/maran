using FluentValidation;
using Maran.Modules.Ssl.Resources;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Ssl.Commands.IssueCertificate;

/// <summary>
/// Validates <see cref="IssueCertificateCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Re-validated inside the agent as well: the API's validation never substitutes for the
/// agent's own boundary check (rules/architecture.md "Agent").
/// </summary>
/// <remarks>
/// The message is a bare resx key, not an English sentence. <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric and then resolves it as an error code
/// against the module's resources; an English sentence is silently discarded and the customer gets
/// the generic failure instead.
///
/// The domain is checked here even though the handler looks it up among the caller's sites: it
/// becomes part of a path the agent canonicalizes, part of a JSON identifier sent to a third party,
/// and part of a rendered vhost.
/// </remarks>
public sealed class IssueCertificateCommandValidator : AbstractValidator<IssueCertificateCommand>
{
    /// <summary>Configures the field rules for <see cref="IssueCertificateCommand"/>.</summary>
    public IssueCertificateCommandValidator()
    {
        RuleFor(command => command.Domain)
            .NotEmpty()
            .MaximumLength(HostNameRule.MaximumLength)
            .Must(HostNameRule.IsHostName)
            .WithMessage(nameof(ErrorMessages.CertificateDomainInvalidFormat));
    }
}
