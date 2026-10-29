using FluentValidation.TestHelper;
using Maran.Modules.Ssl.Commands.IssueCertificate;
using Maran.Modules.Ssl.Tests.TestSupport;

namespace Maran.Modules.Ssl.Tests.Commands.IssueCertificate;

/// <summary>
/// Boundary validation of an issuance order (rules/security.md item 1). The domain becomes part of a
/// path the agent canonicalizes, part of a JSON identifier sent to a third party, and part of a
/// rendered vhost, so its shape is checked here and not only where it is used.
/// </summary>
public sealed class IssueCertificateCommandValidatorTests
{
    /// <summary>The name the stub directory calls the panel's own.</summary>
    private const string PanelName = "panel.example.net";

    /// <summary>The validator under test.</summary>
    private readonly IssueCertificateCommandValidator _validator = new(new StubPanelHostDirectory(PanelName));

    /// <summary>Builds a command with the domain varied.</summary>
    /// <param name="domain">The domain to submit.</param>
    /// <returns>The command.</returns>
    private static IssueCertificateCommand Command(string domain = "example.com")
    {
        return new IssueCertificateCommand(domain, "203.0.113.7", "tests");
    }

    /// <summary>A well formed order passes.</summary>
    [Fact]
    public void A_well_formed_order_passes()
    {
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A domain with a trailing newline is rejected because it would inject an nginx directive.</summary>
    [Theory]
    [InlineData("example.com\n")]
    [InlineData("example.com\r\n")]
    [InlineData("example.com\nserver_name evil.example.com;")]
    public void A_domain_with_a_trailing_newline_is_rejected_because_it_would_inject_an_nginx_directive(
        string domain)
    {
        // In .NET `$` also matches immediately before a trailing newline, so a `$`-anchored pattern
        // accepts the first two of these — and the value is written into a server_name directive.
        _validator.TestValidate(Command(domain))
            .ShouldHaveValidationErrorFor(command => command.Domain);
    }

    /// <summary>A malformed domain is rejected with its resource key as the message.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("nodot")]
    [InlineData("-leadinghyphen.com")]
    [InlineData("trailinghyphen-.com")]
    [InlineData("example..com")]
    [InlineData("example.com/../../etc")]
    public void A_malformed_domain_is_rejected(string domain)
    {
        _validator.TestValidate(Command(domain))
            .ShouldHaveValidationErrorFor(command => command.Domain);
    }

    /// <summary>A domain past the dns ceiling is rejected.</summary>
    [Fact]
    public void A_domain_past_the_dns_ceiling_is_rejected()
    {
        var label = new string('a', 63);
        var tooLong = string.Join('.', Enumerable.Repeat(label, 4));

        _validator.TestValidate(Command(tooLong))
            .ShouldHaveValidationErrorFor(command => command.Domain);
    }

    /// <summary>The rejection message is the resource key the middleware resolves.</summary>
    [Fact]
    public void The_rejection_message_is_the_resource_key_the_middleware_resolves()
    {
        _validator.TestValidate(Command("nodot"))
            .ShouldHaveValidationErrorFor(command => command.Domain)
            .WithErrorMessage("CertificateDomainInvalidFormat");
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
        // Well formed, so the shape rule accepts it; a certificate request for it must still never
        // reach the certificate authority or a vhost.
        _validator.TestValidate(Command(domain))
            .ShouldHaveValidationErrorFor(command => command.Domain)
            .WithErrorMessage("CertificateDomainReserved");
    }

    /// <summary>The panel's own name, its www form and any casing of either are rejected.</summary>
    [Theory]
    [InlineData("panel.example.net")]
    [InlineData("www.panel.example.net")]
    [InlineData("Panel.Example.NET")]
    public void The_panels_own_name_is_rejected_with_the_reserved_key(string domain)
    {
        _validator.TestValidate(Command(domain))
            .ShouldHaveValidationErrorFor(command => command.Domain)
            .WithErrorMessage("CertificateDomainReserved");
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
    public void A_name_that_is_not_reserved_is_accepted(string domain)
    {
        _validator.TestValidate(Command(domain))
            .ShouldNotHaveValidationErrorFor(command => command.Domain);
    }

    /// <summary>With no panel name configured only the static set applies.</summary>
    [Fact]
    public void With_no_panel_name_configured_a_panel_like_name_is_accepted()
    {
        var validator = new IssueCertificateCommandValidator(new StubPanelHostDirectory());

        validator.TestValidate(Command("panel.example.net"))
            .ShouldNotHaveValidationErrorFor(command => command.Domain);
        validator.TestValidate(Command("203.0.113.7"))
            .ShouldHaveValidationErrorFor(command => command.Domain)
            .WithErrorMessage("CertificateDomainReserved");
    }

    /// <summary>A malformed name that also ends in a reserved suffix is reported once, as malformed.</summary>
    [Theory]
    [InlineData("bad..local")]
    [InlineData("under_score.localhost")]
    public void A_malformed_name_is_not_also_reported_as_reserved(string domain)
    {
        var result = _validator.TestValidate(Command(domain));

        var messages = result.Errors.Select(failure =>
        {
            return failure.ErrorMessage;
        }).ToList();
        Assert.Equal(["CertificateDomainInvalidFormat"], messages);
    }
}
