using FluentValidation;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Resources;
using Maran.SharedKernel.Utilities.Network;

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
    /// <summary>
    /// Two-component PHP version as the packages name it, e.g. <c>8.3</c>. Anchored with <c>\z</c>
    /// rather than <c>$</c> for the same reason <see cref="HostNameRule"/> is: in .NET <c>$</c> also
    /// matches immediately before a trailing newline, and this value is written into a config file.
    /// </summary>
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
            .MaximumLength(HostNameRule.MaximumLength)
            .Must(HostNameRule.IsHostName)
            .WithMessage(nameof(ErrorMessages.SiteDomainInvalidFormat));

        RuleFor(command => command.Aliases)
            .NotNull();

        RuleForEach(command => command.Aliases)
            .NotEmpty()
            .MaximumLength(HostNameRule.MaximumLength)
            .Must(HostNameRule.IsHostName)
            .WithMessage(nameof(ErrorMessages.SiteAliasInvalidFormat));

        // A request that names the same hostname twice — an alias repeating the domain, or two
        // identical aliases — is refused here rather than at the database, where the exclusive key
        // on the claimed name would turn it into a fault instead of an answer. Case-insensitive,
        // because Host matching is: "Example.com" and "example.com" are one name.
        RuleFor(command => command.Aliases)
            .Must((command, aliases) =>
            {
                var names = aliases
                    .Select(alias =>
                    {
                        return alias.ToLowerInvariant();
                    })
                    .Append(command.Domain.ToLowerInvariant())
                    .ToList();
                return names.Distinct(StringComparer.Ordinal).Count() == names.Count;
            })
            .WithMessage(nameof(ErrorMessages.SiteAliasDuplicated))
            .When(command =>
            {
                return command.Aliases is not null && command.Domain is not null;
            });

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
