using FluentValidation.TestHelper;
using Maran.Modules.Ssl.Commands.IssueCertificate;

namespace Maran.Modules.Ssl.Tests.Commands.IssueCertificate;

/// <summary>
/// Boundary validation of an issuance order (rules/security.md item 1). The domain becomes part of a
/// path the agent canonicalizes, part of a JSON identifier sent to a third party, and part of a
/// rendered vhost, so its shape is checked here and not only where it is used.
/// </summary>
public sealed class IssueCertificateCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly IssueCertificateCommandValidator _validator = new();

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
}
