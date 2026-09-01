using FluentValidation.TestHelper;
using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Tests.Commands.CreateSite;

/// <summary>
/// Field rules of <see cref="CreateSiteCommandValidator"/>. The domain and its aliases end up in an
/// nginx <c>server_name</c> directive, so what this validator lets through is a config-injection
/// surface, not a formatting preference (rules/security.md item 4).
/// </summary>
public sealed class CreateSiteCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly CreateSiteCommandValidator _validator = new();

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

    /// <summary>Builds a valid PHP-backed command each test varies one field of.</summary>
    private static CreateSiteCommand Command()
    {
        return new CreateSiteCommand(
            Guid.NewGuid(),
            "example.com",
            ["www.example.com"],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            "198.51.100.7",
            "tests");
    }
}
