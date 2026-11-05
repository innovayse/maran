using Maran.Modules.Ssl.Options;
using Maran.Modules.Ssl.Validators;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Ssl.Tests.Validators;

/// <summary>The ACME contact address is held to the panel's one definition of a valid address.</summary>
public sealed class AcmeOptionsValidatorTests
{
    private readonly AcmeOptionsValidator _validator = new();

    /// <summary>The shipped default contact address still passes the stricter shared rule.</summary>
    /// <remarks>
    /// The question the move had to answer before it could be made: adopting a stricter rule on an
    /// installed panel must not turn a default value into a boot failure. <c>admin@localhost</c>
    /// parses, round-trips and carries no control character, so the panel still starts on a server
    /// whose <c>panel.env</c> never set a contact.
    /// </remarks>
    [Fact]
    public void The_shipped_default_contact_address_passes()
    {
        var options = new AcmeOptions();

        Assert.Equal("admin@localhost", options.ContactEmail);
        Assert.True(_validator.Validate(name: null, options).Succeeded);
    }

    /// <summary>An ordinary operator address passes.</summary>
    [Fact]
    public void An_ordinary_operator_address_passes()
    {
        var options = new AcmeOptions { ContactEmail = "ops@example.com" };

        Assert.True(_validator.Validate(name: null, options).Succeeded);
    }

    /// <summary>A display-name wrapped address is refused, which the annotation it replaces accepted.</summary>
    [Fact]
    public void A_display_name_wrapped_contact_address_is_refused()
    {
        var options = new AcmeOptions { ContactEmail = "Ops Team <ops@example.com>" };

        Assert.True(_validator.Validate(name: null, options).Failed);
    }

    /// <summary>A contact address carrying a newline is refused.</summary>
    [Fact]
    public void A_contact_address_carrying_a_newline_is_refused()
    {
        var options = new AcmeOptions { ContactEmail = "ops@example.com\r\nBcc: attacker@example.net" };

        Assert.True(_validator.Validate(name: null, options).Failed);
    }

    /// <summary>A contact address hiding a control character in a quoted local part is refused.</summary>
    /// <remarks>
    /// This address parses and round-trips, so only the control-character sweep refuses it — and it
    /// is registered with the authority and written into mail headers (rules/security.md item 4).
    /// </remarks>
    [Fact]
    public void A_contact_address_hiding_a_control_character_in_a_quoted_local_part_is_refused()
    {
        var options = new AcmeOptions { ContactEmail = "\"a\u0001b\"@example.com" };

        Assert.True(_validator.Validate(name: null, options).Failed);
    }

    /// <summary>A contact address past the standard's ceiling is refused, which the annotation never bounded.</summary>
    [Fact]
    public void A_contact_address_past_the_standards_ceiling_is_refused()
    {
        var email = new string('a', EmailAddressRule.MaximumLength) + "@example.com";
        var options = new AcmeOptions { ContactEmail = email };

        Assert.True(_validator.Validate(name: null, options).Failed);
    }

    /// <summary>The failure names the setting, so an operator can find it in panel.env.</summary>
    [Fact]
    public void The_failure_names_the_setting()
    {
        var options = new AcmeOptions { ContactEmail = "not-an-address" };

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.NotNull(result.FailureMessage);
        Assert.Contains(nameof(AcmeOptions.ContactEmail), result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>Both well-known Let's Encrypt directories pass, the positive control for the refusals below.</summary>
    /// <param name="url">The directory URL.</param>
    [Theory]
    [InlineData("https://acme-v02.api.letsencrypt.org/directory")]
    [InlineData("https://acme-staging-v02.api.letsencrypt.org/directory")]
    public void A_public_https_directory_passes(string url)
    {
        var options = new AcmeOptions { DirectoryUrl = url };

        Assert.True(_validator.Validate(name: null, options).Succeeded);
    }

    /// <summary>A private certificate authority on a private address or port passes.</summary>
    /// <param name="url">The directory URL.</param>
    [Theory]
    [InlineData("https://10.0.0.5:14000/dir")]
    [InlineData("https://ca.internal.example:9000/acme/directory")]
    [InlineData("https://localhost:14000/dir")]
    public void A_private_authority_directory_passes(string url)
    {
        var options = new AcmeOptions { DirectoryUrl = url };

        Assert.True(_validator.Validate(name: null, options).Succeeded);
    }

    /// <summary>A directory URL that is not a plain absolute https URL is refused.</summary>
    /// <param name="url">The directory URL.</param>
    [Theory]
    [InlineData("http://acme-v02.api.letsencrypt.org/directory")]
    [InlineData("ftp://ca.example/directory")]
    [InlineData("/directory")]
    [InlineData("acme-v02.api.letsencrypt.org/directory")]
    [InlineData("https://")]
    [InlineData("https://user:secret@ca.example/directory")]
    [InlineData("https://user@ca.example/directory")]
    [InlineData("https://ca.example/directory#frag")]
    [InlineData("https://ca.example/directory#")]
    [InlineData("https://ca.example/dir ectory")]
    [InlineData(" https://ca.example/directory")]
    [InlineData("https://ca.example/directory\r\nHost: evil")]
    [InlineData("https://ca.example/dir\u0001ectory")]
    [InlineData("")]
    public void A_directory_that_is_not_a_plain_absolute_https_url_is_refused(string url)
    {
        var options = new AcmeOptions { DirectoryUrl = url };

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.NotNull(result.FailureMessage);
        Assert.Contains(nameof(AcmeOptions.DirectoryUrl), result.FailureMessage, StringComparison.Ordinal);
    }
}
