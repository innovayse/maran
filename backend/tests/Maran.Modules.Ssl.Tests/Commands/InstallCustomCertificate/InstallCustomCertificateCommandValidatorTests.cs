using FluentValidation.TestHelper;
using Maran.Modules.Ssl.Commands.InstallCustomCertificate;
using Maran.Modules.Ssl.Tests.TestSupport;

namespace Maran.Modules.Ssl.Tests.Commands.InstallCustomCertificate;

/// <summary>Boundary validation of the customer-supplied install (rules/security.md item 1).</summary>
public sealed class InstallCustomCertificateCommandValidatorTests
{
    /// <summary>The name the stub directory calls the panel's own.</summary>
    private const string PanelName = "panel.example.net";

    /// <summary>The validator under test.</summary>
    private readonly InstallCustomCertificateCommandValidator _validator = new(new StubPanelHostDirectory(PanelName));

    /// <summary>Builds a command with one field varied.</summary>
    /// <param name="domain">The domain to submit.</param>
    /// <param name="certificatePem">The certificate text to submit.</param>
    /// <param name="privateKeyPem">The key text to submit.</param>
    /// <returns>The command.</returns>
    private static InstallCustomCertificateCommand Command(
        string domain = "example.com",
        string certificatePem = "-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----",
        string privateKeyPem = "-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----")
    {
        return new InstallCustomCertificateCommand(domain, certificatePem, privateKeyPem, "203.0.113.7", "tests");
    }

    /// <summary>A well formed submission passes.</summary>
    [Fact]
    public void A_well_formed_submission_passes()
    {
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A domain with a trailing newline is rejected because it would inject an nginx directive.</summary>
    [Fact]
    public void A_domain_with_a_trailing_newline_is_rejected_because_it_would_inject_an_nginx_directive()
    {
        // In .NET `$` also matches before a trailing newline, so an anchor of `$` would let this
        // through — and the value is written into a server_name directive.
        _validator.TestValidate(Command(domain: "example.com\n"))
            .ShouldHaveValidationErrorFor(command => command.Domain);
    }

    /// <summary>A domain carrying a path separator is rejected.</summary>
    [Fact]
    public void A_domain_carrying_a_path_separator_is_rejected()
    {
        _validator.TestValidate(Command(domain: "example.com/../../etc"))
            .ShouldHaveValidationErrorFor(command => command.Domain);
    }

    /// <summary>A single label domain is rejected.</summary>
    [Fact]
    public void A_single_label_domain_is_rejected()
    {
        _validator.TestValidate(Command(domain: "localhost"))
            .ShouldHaveValidationErrorFor(command => command.Domain);
    }

    /// <summary>Text that is not an armoured certificate is rejected.</summary>
    [Fact]
    public void Text_that_is_not_an_armoured_certificate_is_rejected()
    {
        _validator.TestValidate(Command(certificatePem: "just some text"))
            .ShouldHaveValidationErrorFor(command => command.CertificatePem);
    }

    /// <summary>An empty key is rejected.</summary>
    [Fact]
    public void An_empty_key_is_rejected()
    {
        _validator.TestValidate(Command(privateKeyPem: string.Empty))
            .ShouldHaveValidationErrorFor(command => command.PrivateKeyPem);
    }

    /// <summary>A key exported in any of the usual armours is accepted.</summary>
    [Theory]
    [InlineData("-----BEGIN PRIVATE KEY-----\nk\n-----END PRIVATE KEY-----")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nk\n-----END RSA PRIVATE KEY-----")]
    [InlineData("-----BEGIN EC PRIVATE KEY-----\nk\n-----END EC PRIVATE KEY-----")]
    public void A_key_exported_in_any_of_the_usual_armours_is_accepted(string privateKeyPem)
    {
        // Pinning one armour here would reject a perfectly good certificate for having been
        // exported by a different tool; the agent verifies the key against the certificate.
        _validator.TestValidate(Command(privateKeyPem: privateKeyPem))
            .ShouldNotHaveValidationErrorFor(command => command.PrivateKeyPem);
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
        _validator.TestValidate(Command(domain: domain))
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
        _validator.TestValidate(Command(domain: domain))
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
        _validator.TestValidate(Command(domain: domain))
            .ShouldNotHaveValidationErrorFor(command => command.Domain);
    }

    /// <summary>With no panel name configured only the static set applies.</summary>
    [Fact]
    public void With_no_panel_name_configured_a_panel_like_name_is_accepted()
    {
        var validator = new InstallCustomCertificateCommandValidator(new StubPanelHostDirectory());

        validator.TestValidate(Command(domain: "panel.example.net"))
            .ShouldNotHaveValidationErrorFor(command => command.Domain);
        validator.TestValidate(Command(domain: "203.0.113.7"))
            .ShouldHaveValidationErrorFor(command => command.Domain)
            .WithErrorMessage("CertificateDomainReserved");
    }

    /// <summary>A malformed name that also ends in a reserved suffix is reported once, as malformed.</summary>
    [Theory]
    [InlineData("bad..local")]
    [InlineData("under_score.localhost")]
    public void A_malformed_name_is_not_also_reported_as_reserved(string domain)
    {
        var result = _validator.TestValidate(Command(domain: domain));

        var messages = result.Errors.Select(failure =>
        {
            return failure.ErrorMessage;
        }).ToList();
        Assert.Equal(["CertificateDomainInvalidFormat"], messages);
    }
}
