using FluentValidation;
using Maran.Modules.Ssl.Resources;

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
    /// <summary>A hostname of two or more DNS labels: 1–63 characters each, no leading or trailing hyphen.</summary>
    /// <remarks>
    /// Anchored with <c>\z</c> rather than <c>$</c>. In .NET <c>$</c> also matches immediately before
    /// a trailing newline, so <c>example.com\n</c> satisfies a <c>$</c>-anchored pattern — and this
    /// value is written into an nginx <c>server_name</c> directive, where an embedded newline is a
    /// config-injection primitive (rules/security.md item 4).
    /// </remarks>
    private const string HostnamePattern =
        @"\A(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))+\z";

    /// <summary>Configures the field rules for <see cref="IssueCertificateCommand"/>.</summary>
    public IssueCertificateCommandValidator()
    {
        RuleFor(command => command.Domain)
            .NotEmpty()
            .MaximumLength(253)
            .Matches(HostnamePattern)
            .WithMessage(nameof(ErrorMessages.CertificateDomainInvalidFormat));
    }
}
