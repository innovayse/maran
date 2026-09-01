using FluentValidation;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Resources;

namespace Maran.Modules.Sites.Commands.CreateSite;

/// <summary>
/// Validates <see cref="CreateSiteCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Every one of these is re-validated inside the agent as well: the API's validation
/// never substitutes for the agent's own boundary check (rules/architecture.md "Agent").
/// </summary>
/// <remarks>
/// Each message is a bare resx key, not an English sentence. <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric, and then resolves it as an error code
/// against the module's resources; an English sentence is silently discarded and the customer gets
/// the generic failure instead — so a hardcoded sentence here is both a rules/csharp.md reject and
/// dead code.
/// </remarks>
public sealed class CreateSiteCommandValidator : AbstractValidator<CreateSiteCommand>
{
    /// <summary>A hostname of two or more DNS labels: 1–63 characters each, no leading or trailing hyphen.</summary>
    /// <remarks>
    /// Anchored with <c>\z</c> rather than <c>$</c>. In .NET <c>$</c> also matches immediately before
    /// a trailing newline, so <c>example.com\n</c> satisfies a <c>$</c>-anchored pattern — and this
    /// value is written into an nginx <c>server_name</c> directive, where an embedded newline is a
    /// config-injection primitive. rules/security.md item 4 requires the boundary to reject it.
    /// </remarks>
    private const string HostnamePattern =
        @"\A(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))+\z";

    /// <summary>Two-component PHP version as the packages name it, e.g. <c>8.3</c>. Anchored like the hostname, and for the same reason.</summary>
    private const string PhpVersionPattern = @"\A[0-9]\.[0-9]{1,2}\z";

    /// <summary>An upstream nginx will proxy to: host or host:port, with no scheme, path or whitespace.</summary>
    private const string ProxyUpstreamPattern = @"\A[A-Za-z0-9.-]{1,253}(:[0-9]{1,5})?\z";

    /// <summary>Configures the field rules for <see cref="CreateSiteCommand"/>.</summary>
    public CreateSiteCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Domain)
            .NotEmpty()
            .MaximumLength(253)
            .Matches(HostnamePattern)
            .WithMessage(nameof(ErrorMessages.SiteDomainInvalidFormat));

        RuleFor(command => command.Aliases)
            .NotNull();

        RuleForEach(command => command.Aliases)
            .NotEmpty()
            .MaximumLength(253)
            .Matches(HostnamePattern)
            .WithMessage(nameof(ErrorMessages.SiteAliasInvalidFormat));

        RuleFor(command => command.BackendType)
            .IsInEnum();

        // The version is only meaningful for a PHP backend, and only a shape is checked here —
        // whether the host actually HAS it is a question for the agent's installed list, answered
        // by the handler as PhpVersionNotInstalled rather than as a validation message.
        RuleFor(command => command.PhpVersion)
            .NotEmpty()
            .Matches(PhpVersionPattern)
            .WithMessage(nameof(ErrorMessages.PhpVersionInvalidFormat))
            .When(command =>
            {
                return command.BackendType == SiteBackendType.Php;
            });

        RuleFor(command => command.ProxyUpstream)
            .NotEmpty()
            .MaximumLength(253)
            .Matches(ProxyUpstreamPattern)
            .WithMessage(nameof(ErrorMessages.SiteProxyUpstreamInvalidFormat))
            .When(command =>
            {
                return command.BackendType == SiteBackendType.ReverseProxy;
            });
    }
}
