using FluentValidation.TestHelper;
using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Commands.CreateSite;

/// <summary>
/// Field rules of <see cref="CreateSiteCommandValidator"/>. The domain and its aliases end up in an
/// nginx <c>server_name</c> directive, so what this validator lets through is a config-injection
/// surface, not a formatting preference (rules/security.md item 4).
/// </summary>
public sealed class CreateSiteCommandValidatorTests
{
    /// <summary>The name the stub directory calls the panel's own.</summary>
    private const string PanelName = "panel.example.net";

    /// <summary>The validator under test.</summary>
    private readonly CreateSiteCommandValidator _validator = new(new StubPanelHostDirectory(PanelName));

    /// <summary>A well formed php site passes every rule.</summary>
    [Fact]
    public void A_well_formed_php_site_passes_every_rule()
    {
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A domain with a trailing newline is rejected.</summary>
    [Theory]
    [InlineData("example.com\n")]
    [InlineData("example.com\r\n")]
    [InlineData("example.com\nserver_name evil.example.com;")]
    public void A_domain_with_a_trailing_newline_is_rejected(string domain)
    {
        // .NET's `$` also matches immediately before a trailing newline, so a `$`-anchored pattern
        // accepts every one of these — and the value is written into an nginx directive.
        _validator.TestValidate(Command() with { Domain = domain })
            .ShouldHaveValidationErrorFor(command => command.Domain);
    }

    /// <summary>A malformed domain is rejected with its resource key as the message.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("nodot")]
    [InlineData("-leadinghyphen.com")]
    [InlineData("trailinghyphen-.com")]
    [InlineData("has space.com")]
    [InlineData("under_score.com")]
    public void A_malformed_domain_is_rejected(string domain)
    {
        _validator.TestValidate(Command() with { Domain = domain })
            .ShouldHaveValidationErrorFor(command => command.Domain);
    }

    /// <summary>A rejected domain reports a resource key rather than an english sentence.</summary>
    [Fact]
    public void A_rejected_domain_reports_a_resource_key_rather_than_an_english_sentence()
    {
        // ExceptionMiddleware forwards a validation message only when it is entirely alphanumeric,
        // and then resolves it as an error code. An English sentence is discarded, and the customer
        // sees the generic failure — so the key IS the mechanism, not decoration.
        var result = _validator.TestValidate(Command() with { Domain = "nodot" });

        var message = result.Errors[0].ErrorMessage;
        Assert.Equal("SiteDomainInvalidFormat", message);
        Assert.True(message.All(char.IsLetterOrDigit));
    }

    /// <summary>A malformed alias is rejected.</summary>
    [Theory]
    [InlineData("www.example.com\n")]
    [InlineData("nodot")]
    [InlineData("")]
    public void A_malformed_alias_is_rejected(string alias)
    {
        var result = _validator.TestValidate(Command() with { Aliases = [alias] });

        Assert.NotEmpty(result.Errors);
    }

    /// <summary>A hostname listed twice in one request is rejected.</summary>
    [Theory]
    [InlineData("example.com")]
    [InlineData("Example.COM")]
    [InlineData("www.example.com")]
    public void A_hostname_listed_twice_in_one_request_is_rejected(string alias)
    {
        // Each of these repeats a name the same request already claims — the domain, the domain in
        // another case, or the first alias. The claim is one row per name, so a request naming one
        // twice would reach the database as a duplicate key and surface as a fault instead of an
        // answer.
        var result = _validator.TestValidate(Command() with { Aliases = ["www.example.com", alias] });

        Assert.NotEmpty(result.Errors);
    }

    /// <summary>A domain in the static reserved set is rejected with the reserved key.</summary>
    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("127.0.0.1")]
    [InlineData("shop.123")]
    [InlineData("foo.localhost")]
    [InlineData("FOO.LOCAL")]
    [InlineData("x.localdomain")]
    [InlineData("x.internal")]
    [InlineData("x.lan")]
    public void A_domain_in_the_reserved_set_is_rejected_with_the_reserved_key(string domain)
    {
        // Well formed, so the shape rule accepts every one of these; each would still land in an
        // nginx server_name on a root-managed host.
        var result = _validator.TestValidate(Command() with { Domain = domain });

        result.ShouldHaveValidationErrorFor(command => command.Domain)
            .WithErrorMessage("SiteDomainReserved");
    }

    /// <summary>The panel's own name, its www form and any casing of either are rejected as a domain.</summary>
    [Theory]
    [InlineData("panel.example.net")]
    [InlineData("www.panel.example.net")]
    [InlineData("Panel.Example.NET")]
    public void The_panels_own_name_is_rejected_as_a_domain_with_the_reserved_key(string domain)
    {
        _validator.TestValidate(Command() with { Domain = domain })
            .ShouldHaveValidationErrorFor(command => command.Domain)
            .WithErrorMessage("SiteDomainReserved");
    }

    /// <summary>A name beside the panel's, or merely resembling a reserved one, is accepted.</summary>
    /// <remarks>The inverse control: a validator that refused everything would pass every rejection test.</remarks>
    [Theory]
    [InlineData("shop.panel.example.net")]
    [InlineData("example.net")]
    [InlineData("mylocal.com")]
    [InlineData("localhost.example.com")]
    [InlineData("internal.example.com")]
    [InlineData("web2.example.com")]
    [InlineData("shop.example.test")]
    public void A_name_that_is_not_reserved_is_accepted_as_a_domain(string domain)
    {
        _validator.TestValidate(Command() with { Domain = domain })
            .ShouldNotHaveValidationErrorFor(command => command.Domain);
    }

    /// <summary>An alias in the static reserved set is rejected with the alias reserved key.</summary>
    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("foo.localhost")]
    [InlineData("x.local")]
    public void An_alias_in_the_reserved_set_is_rejected_with_the_alias_reserved_key(string alias)
    {
        var result = _validator.TestValidate(Command() with { Aliases = ["www.example.com", alias] });

        Assert.Contains(result.Errors, failure =>
        {
            return failure.PropertyName == "Aliases[1]" && failure.ErrorMessage == "SiteAliasReserved";
        });
        Assert.DoesNotContain(result.Errors, failure =>
        {
            return failure.PropertyName == "Aliases[0]";
        });
    }

    /// <summary>The panel's own name is rejected as an alias.</summary>
    [Theory]
    [InlineData("panel.example.net")]
    [InlineData("WWW.panel.example.net")]
    public void The_panels_own_name_is_rejected_as_an_alias_with_the_alias_reserved_key(string alias)
    {
        var result = _validator.TestValidate(Command() with { Aliases = [alias] });

        Assert.Contains(result.Errors, failure =>
        {
            return failure.PropertyName == "Aliases[0]" && failure.ErrorMessage == "SiteAliasReserved";
        });
    }

    /// <summary>An ordinary alias beside the domain is accepted.</summary>
    [Fact]
    public void An_ordinary_alias_is_accepted()
    {
        var result = _validator.TestValidate(Command() with { Aliases = ["shop.example.com", "shop.panel.example.net"] });

        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>With no panel name configured only the static set applies.</summary>
    [Fact]
    public void With_no_panel_name_configured_a_panel_like_name_is_accepted()
    {
        var validator = new CreateSiteCommandValidator(new StubPanelHostDirectory());

        validator.TestValidate(Command() with { Domain = "panel.example.net" })
            .ShouldNotHaveValidationErrorFor(command => command.Domain);
        validator.TestValidate(Command() with { Domain = "203.0.113.7" })
            .ShouldHaveValidationErrorFor(command => command.Domain)
            .WithErrorMessage("SiteDomainReserved");
    }

    /// <summary>A malformed name that also ends in a reserved suffix is reported once, as malformed.</summary>
    [Theory]
    [InlineData("bad..local")]
    [InlineData("under_score.localhost")]
    public void A_malformed_name_is_not_also_reported_as_reserved(string name)
    {
        var domainResult = _validator.TestValidate(Command() with { Domain = name });
        var aliasResult = _validator.TestValidate(Command() with { Aliases = [name] });

        Assert.Equal(["SiteDomainInvalidFormat"], Messages(domainResult.Errors));
        Assert.Equal(["SiteAliasInvalidFormat"], Messages(aliasResult.Errors));
    }

    /// <summary>A php site without a well formed version is rejected.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("8")]
    [InlineData("8.3.1")]
    [InlineData("8.3\n")]
    [InlineData("latest")]
    public void A_php_site_without_a_well_formed_version_is_rejected(string version)
    {
        _validator.TestValidate(Command() with { PhpVersion = version })
            .ShouldHaveValidationErrorFor(command => command.PhpVersion);
    }

    /// <summary>A static site needs no php version.</summary>
    [Fact]
    public void A_static_site_needs_no_php_version()
    {
        var command = Command() with { BackendType = SiteBackendType.Static, PhpVersion = string.Empty };

        _validator.TestValidate(command).ShouldNotHaveValidationErrorFor(c => c.PhpVersion);
    }

    /// <summary>A reverse proxy site without a well formed upstream is rejected.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("http://backend:8080")]
    [InlineData("backend:8080/path")]
    [InlineData("backend\n")]
    public void A_reverse_proxy_site_without_a_well_formed_upstream_is_rejected(string upstream)
    {
        var command = Command() with
        {
            BackendType = SiteBackendType.ReverseProxy,
            PhpVersion = string.Empty,
            ProxyUpstream = upstream,
        };

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.ProxyUpstream);
    }

    /// <summary>A reverse proxy site with a host and port passes.</summary>
    [Fact]
    public void A_reverse_proxy_site_with_a_host_and_port_passes()
    {
        var command = Command() with
        {
            BackendType = SiteBackendType.ReverseProxy,
            PhpVersion = string.Empty,
            ProxyUpstream = "127.0.0.1:8080",
        };

        _validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A site with no account is rejected.</summary>
    [Fact]
    public void A_site_with_no_account_is_rejected()
    {
        _validator.TestValidate(Command() with { AccountId = Guid.Empty })
            .ShouldHaveValidationErrorFor(command => command.AccountId);
    }

    /// <summary>A backend outside the enum is rejected.</summary>
    [Fact]
    public void A_backend_outside_the_enum_is_rejected()
    {
        _validator.TestValidate(Command() with { BackendType = (SiteBackendType)99 })
            .ShouldHaveValidationErrorFor(command => command.BackendType);
    }

    /// <summary>The message of each failure, in order.</summary>
    /// <param name="failures">The failures a validation produced.</param>
    /// <returns>The messages, which are resource keys.</returns>
    private static List<string> Messages(IEnumerable<FluentValidation.Results.ValidationFailure> failures)
    {
        return failures.Select(failure =>
        {
            return failure.ErrorMessage;
        }).ToList();
    }

    /// <summary>Builds a valid PHP-backed command each test varies one field of.</summary>
    private static CreateSiteCommand Command()
    {
        return new CreateSiteCommand(
            Guid.NewGuid(),
            "example.com",
            SiteBackendType.Php,
            ["www.example.com"],
            "8.3",
            string.Empty,
            "198.51.100.7",
            "tests");
    }
}
