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
}
