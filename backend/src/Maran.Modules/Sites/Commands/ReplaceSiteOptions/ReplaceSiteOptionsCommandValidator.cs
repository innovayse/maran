using FluentValidation;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Resources;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Sites.Commands.ReplaceSiteOptions;

/// <summary>
/// Validates <see cref="ReplaceSiteOptionsCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Every value is written into a web-server configuration by a root process, so each is held
/// to an allow-list of shapes here and again by the agent (rules/architecture.md "Agent").
/// </summary>
/// <remarks>
/// <para>
/// Each message is a bare resx key, for the reason spelled out on
/// <see cref="CreateSite.CreateSiteCommandValidator"/>: a sentence never reaches the customer.
/// </para>
/// <para>
/// What it cannot decide is left to the handler because it needs the stored site: whether a canonical
/// host can be served at all, and whether a user without a password already has one.
/// </para>
/// </remarks>
public sealed class ReplaceSiteOptionsCommandValidator : AbstractValidator<ReplaceSiteOptionsCommand>
{
    /// <summary>Configures the field rules for <see cref="ReplaceSiteOptionsCommand"/>.</summary>
    public ReplaceSiteOptionsCommandValidator()
    {
        RuleFor(command => command.SiteId)
            .NotEmpty();

        RuleFor(command => command.CanonicalHost)
            .IsInEnum()
            .WithMessage(nameof(ErrorMessages.SiteOptionUnknown));

        RuleFor(command => command.ClientMaxBodySizeMib)
            .InclusiveBetween(SiteOptionsLimits.MinimumBodySizeMib, SiteOptionsLimits.MaximumBodySizeMib)
            .WithMessage(nameof(ErrorMessages.SiteBodySizeOutOfRange))
            .When(command =>
            {
                return command.ClientMaxBodySizeMib is not null;
            });

        RuleFor(command => command.StaticCacheDays)
            .InclusiveBetween(SiteOptionsLimits.MinimumCacheDays, SiteOptionsLimits.MaximumCacheDays)
            .WithMessage(nameof(ErrorMessages.SiteCacheDaysOutOfRange))
            .When(command =>
            {
                return command.StaticCacheDays is not null;
            });

        ConfigureRedirects();
        ConfigureBasicAuth();
        ConfigureIpRules();
        ConfigureRequestRate();
    }

    /// <summary>Adds the rules for the redirect list and each rule in it.</summary>
    private void ConfigureRedirects()
    {
        RuleFor(command => command.Redirects)
            .NotNull();

        RuleFor(command => command.Redirects.Count)
            .LessThanOrEqualTo(SiteOptionsLimits.MaximumRedirects)
            .OverridePropertyName(nameof(ReplaceSiteOptionsCommand.Redirects))
            .WithMessage(nameof(ErrorMessages.SiteRedirectLimit))
            .When(command =>
            {
                return command.Redirects is not null;
            });

        RuleForEach(command => command.Redirects)
            .ChildRules(redirect =>
            {
                redirect.RuleFor(rule => rule.Path)
                    .Must(SiteRedirectRules.IsValidPath)
                    .WithMessage(nameof(ErrorMessages.SiteRedirectPathInvalid));

                redirect.RuleFor(rule => rule.Match)
                    .IsInEnum()
                    .WithMessage(nameof(ErrorMessages.SiteOptionUnknown));

                redirect.RuleFor(rule => rule.Status)
                    .IsInEnum()
                    .WithMessage(nameof(ErrorMessages.SiteOptionUnknown));

                redirect.RuleFor(rule => rule.Target)
                    .Must(SiteRedirectRules.IsValidTarget)
                    .WithMessage(nameof(ErrorMessages.SiteRedirectTargetInvalid));

                // A prefix rule names a directory: without the trailing slash "/blog" would also
                // capture "/blogging", which is never what a customer meant by a folder.
                redirect.RuleFor(rule => rule.Path)
                    .Must(SiteRedirectRules.IsValidPrefix)
                    .WithMessage(nameof(ErrorMessages.SiteRedirectPathInvalid))
                    .When(rule =>
                    {
                        return rule.Match == RedirectMatch.Prefix && SiteRedirectRules.IsValidPath(rule.Path);
                    });

                // An exact rule that sends a path to itself would loop for ever.
                redirect.RuleFor(rule => rule.Target)
                    .Must((rule, target) =>
                    {
                        return !string.Equals(rule.Path, target, StringComparison.Ordinal);
                    })
                    .WithMessage(nameof(ErrorMessages.SiteRedirectTargetInvalid))
                    .When(rule =>
                    {
                        return rule.Match == RedirectMatch.Exact;
                    });
            })
            .When(command =>
            {
                return command.Redirects is not null;
            });

        // Two rules for one path and comparison would leave the rendered order deciding which one a
        // visitor gets, and the customer reading the list would not know either.
        RuleFor(command => command.Redirects)
            .Must(redirects =>
            {
                var keys = redirects
                    .Select(rule =>
                    {
                        return (rule.Path, rule.Match);
                    })
                    .ToList();
                return keys.Distinct().Count() == keys.Count;
            })
            .WithMessage(nameof(ErrorMessages.SiteRedirectDuplicated))
            .When(command =>
            {
                return command.Redirects is not null;
            });
    }

    /// <summary>Adds the rules for the basic-authentication list and each user in it.</summary>
    private void ConfigureBasicAuth()
    {
        RuleFor(command => command.BasicAuthUsers)
            .NotNull();

        RuleFor(command => command.BasicAuthUsers.Count)
            .LessThanOrEqualTo(SiteOptionsLimits.MaximumBasicAuthUsers)
            .OverridePropertyName(nameof(ReplaceSiteOptionsCommand.BasicAuthUsers))
            .WithMessage(nameof(ErrorMessages.SiteBasicAuthLimit))
            .When(command =>
            {
                return command.BasicAuthUsers is not null;
            });

        RuleForEach(command => command.BasicAuthUsers)
            .ChildRules(user =>
            {
                user.RuleFor(entry => entry.Name)
                    .NotEmpty()
                    .Matches(SiteOptionsLimits.UserNamePattern)
                    .WithMessage(nameof(ErrorMessages.SiteBasicAuthUserInvalid));

                // Only a password that is stated is checked: an empty one means "keep the stored hash",
                // and the handler refuses it for a user who has none.
                user.RuleFor(entry => entry.Password)
                    .Must(BasicAuthPasswordHash.IsAcceptablePassword)
                    .WithMessage(nameof(ErrorMessages.SiteBasicAuthPasswordInvalid))
                    .When(entry =>
                    {
                        return !string.IsNullOrEmpty(entry.Password);
                    });
            })
            .When(command =>
            {
                return command.BasicAuthUsers is not null;
            });

        // Case-insensitive, although the web server compares names exactly: "Alice" and "alice" in one
        // list is a mistake the customer would not see until the wrong one failed to log in.
        RuleFor(command => command.BasicAuthUsers)
            .Must(users =>
            {
                var names = users
                    .Select(user =>
                    {
                        return user.Name.ToLowerInvariant();
                    })
                    .ToList();
                return names.Distinct(StringComparer.Ordinal).Count() == names.Count;
            })
            .WithMessage(nameof(ErrorMessages.SiteBasicAuthUserDuplicated))
            .When(command =>
            {
                return command.BasicAuthUsers is not null
                    && command.BasicAuthUsers.All(user =>
                    {
                        return user.Name is not null;
                    });
            });
    }

    /// <summary>Adds the rules for the IP rule list and its mode.</summary>
    private void ConfigureIpRules()
    {
        RuleFor(command => command.IpRules)
            .NotNull();

        RuleFor(command => command.IpRules.Count)
            .LessThanOrEqualTo(SiteOptionsLimits.MaximumIpRules)
            .OverridePropertyName(nameof(ReplaceSiteOptionsCommand.IpRules))
            .WithMessage(nameof(ErrorMessages.SiteIpRuleLimit))
            .When(command =>
            {
                return command.IpRules is not null;
            });

        RuleForEach(command => command.IpRules)
            .Must(rule =>
            {
                return CidrRule.TryParse(rule, out _);
            })
            .WithMessage(nameof(ErrorMessages.SiteIpRuleInvalid))
            .When(command =>
            {
                return command.IpRules is not null;
            });

        // Compared by what the rules MEAN, not by how they are spelled: "203.0.113.5" and
        // "203.0.113.5/32" are one rule written twice.
        RuleFor(command => command.IpRules)
            .Must(rules =>
            {
                var normalised = rules
                    .Select(NormalisedRule)
                    .ToList();
                return normalised.Distinct(StringComparer.Ordinal).Count() == normalised.Count;
            })
            .WithMessage(nameof(ErrorMessages.SiteIpRuleDuplicated))
            .When(command =>
            {
                return command.IpRules is not null
                    && command.IpRules.All(rule =>
                    {
                        return CidrRule.TryParse(rule, out _);
                    });
            });

        // The mode is meaningful exactly when there is a list to apply it to.
        RuleFor(command => command.IpAccessMode)
            .Must(mode =>
            {
                return mode is { } named && Enum.IsDefined(named);
            })
            .WithMessage(nameof(ErrorMessages.SiteIpAccessModeInvalid))
            .When(command =>
            {
                return command.IpRules is { Count: > 0 };
            });

        RuleFor(command => command.IpAccessMode)
            .Null()
            .WithMessage(nameof(ErrorMessages.SiteIpAccessModeInvalid))
            .When(command =>
            {
                return command.IpRules is { Count: 0 };
            });
    }

    /// <summary>Adds the rules for the request limit, when one is stated.</summary>
    private void ConfigureRequestRate()
    {
        RuleFor(command => command.RequestRate!.PerSecond)
            .InclusiveBetween(SiteOptionsLimits.MinimumRequestsPerSecond, SiteOptionsLimits.MaximumRequestsPerSecond)
            .OverridePropertyName(nameof(ReplaceSiteOptionsCommand.RequestRate))
            .WithMessage(nameof(ErrorMessages.SiteRequestRateOutOfRange))
            .When(command =>
            {
                return command.RequestRate is not null;
            });

        RuleFor(command => command.RequestRate!.Burst)
            .InclusiveBetween(SiteOptionsLimits.MinimumRequestBurst, SiteOptionsLimits.MaximumRequestBurst)
            .OverridePropertyName(nameof(ReplaceSiteOptionsCommand.RequestRate))
            .WithMessage(nameof(ErrorMessages.SiteRequestBurstOutOfRange))
            .When(command =>
            {
                return command.RequestRate is not null;
            });
    }

    /// <summary>Spells a rule that has already been parsed successfully in its normal form.</summary>
    /// <param name="rule">A rule <see cref="CidrRule.TryParse"/> accepted.</param>
    /// <returns>The normal form <c>address/prefix</c>.</returns>
    /// <exception cref="InvalidOperationException">The rule does not parse, which the caller's own check rules out.</exception>
    private static string NormalisedRule(string rule)
    {
        if (!CidrRule.TryParse(rule, out var parsed))
        {
            throw new InvalidOperationException("A rule that was already accepted no longer parses.");
        }

        return parsed!.ToString();
    }
}
