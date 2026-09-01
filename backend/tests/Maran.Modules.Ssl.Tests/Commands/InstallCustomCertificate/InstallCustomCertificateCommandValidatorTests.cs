using FluentValidation.TestHelper;
using Maran.Modules.Ssl.Commands.InstallCustomCertificate;

namespace Maran.Modules.Ssl.Tests.Commands.InstallCustomCertificate;

/// <summary>Boundary validation of the customer-supplied install (rules/security.md item 1).</summary>
public sealed class InstallCustomCertificateCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly InstallCustomCertificateCommandValidator _validator = new();

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
}
