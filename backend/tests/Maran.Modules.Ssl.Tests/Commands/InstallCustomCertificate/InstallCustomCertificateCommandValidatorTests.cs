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

    /// <summary>A missing certificate is reported as missing, under the certificate field, and as nothing else.</summary>
    [Fact]
    public void A_missing_certificate_is_reported_as_missing_and_as_nothing_else()
    {
        var result = _validator.TestValidate(Command(certificatePem: string.Empty));

        Assert.Equal(["CertificatePemRequired"], MessagesFor(result, nameof(InstallCustomCertificateCommand.CertificatePem)));
    }

    /// <summary>A missing key is reported as missing, under the key field, and as nothing else.</summary>
    [Fact]
    public void A_missing_key_is_reported_as_missing_and_as_nothing_else()
    {
        var result = _validator.TestValidate(Command(privateKeyPem: string.Empty));

        Assert.Equal(["PrivateKeyPemRequired"], MessagesFor(result, nameof(InstallCustomCertificateCommand.PrivateKeyPem)));
    }

    /// <summary>Certificate text that is present but not PEM is reported as invalid, not as missing.</summary>
    [Fact]
    public void Certificate_text_that_is_not_pem_is_reported_as_invalid_not_as_missing()
    {
        var result = _validator.TestValidate(Command(certificatePem: "just some text"));

        Assert.Equal(["CertificatePemInvalid"], MessagesFor(result, nameof(InstallCustomCertificateCommand.CertificatePem)));
    }

    /// <summary>Key text that is present but not a PEM private key is reported as invalid, not as missing.</summary>
    [Theory]
    [InlineData("just some text")]
    [InlineData("-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nk\n-----END PUBLIC KEY-----")]
    public void Key_text_that_is_not_a_pem_private_key_is_reported_as_invalid_not_as_missing(string privateKeyPem)
    {
        var result = _validator.TestValidate(Command(privateKeyPem: privateKeyPem));

        Assert.Equal(["PrivateKeyPemInvalid"], MessagesFor(result, nameof(InstallCustomCertificateCommand.PrivateKeyPem)));
    }

    /// <summary>Material over the size limit is reported as too large, under its own field.</summary>
    [Fact]
    public void Material_over_the_size_limit_is_reported_as_too_large_under_its_own_field()
    {
        var oversized = "-----BEGIN CERTIFICATE-----\n" + new string('A', 65536);

        var certificate = _validator.TestValidate(Command(certificatePem: oversized));
        var key = _validator.TestValidate(Command(privateKeyPem: oversized.Replace("CERTIFICATE", "PRIVATE KEY", StringComparison.Ordinal)));

        Assert.Equal(["CertificatePemTooLarge"], MessagesFor(certificate, nameof(InstallCustomCertificateCommand.CertificatePem)));
        Assert.Equal(["PrivateKeyPemTooLarge"], MessagesFor(key, nameof(InstallCustomCertificateCommand.PrivateKeyPem)));
    }

    /// <summary>An encrypted key, and a key file that opens with an EC PARAMETERS block, both count as PEM keys.</summary>
    [Theory]
    [InlineData("-----BEGIN ENCRYPTED PRIVATE KEY-----\nk\n-----END ENCRYPTED PRIVATE KEY-----")]
    [InlineData("-----BEGIN EC PARAMETERS-----\np\n-----END EC PARAMETERS-----\n-----BEGIN EC PRIVATE KEY-----\nk\n-----END EC PRIVATE KEY-----")]
    public void An_encrypted_key_or_one_after_ec_parameters_counts_as_a_pem_key(string privateKeyPem)
    {
        // Whether openssl can then READ it is the agent's verdict, answered with its own code.
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

    /// <summary>The codes one field was refused with, in the order the validator raised them.</summary>
    /// <param name="result">The validation outcome.</param>
    /// <param name="property">The command member to read the failures of.</param>
    /// <returns>Every error message raised for that member.</returns>
    private static List<string> MessagesFor(TestValidationResult<InstallCustomCertificateCommand> result, string property)
    {
        return result.Errors
            .Where(failure =>
            {
                return failure.PropertyName == property;
            })
            .Select(failure =>
            {
                return failure.ErrorMessage;
            })
            .ToList();
    }
}
