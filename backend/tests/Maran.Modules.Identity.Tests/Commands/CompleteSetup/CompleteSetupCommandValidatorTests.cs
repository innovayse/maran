using FluentValidation.TestHelper;
using Maran.Modules.Identity.Commands.CompleteSetup;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Identity.Tests.Commands.CompleteSetup;
/// <summary>Behavioural contract of complete setup command validator.</summary>

public sealed class CompleteSetupCommandValidatorTests
{
    private readonly CompleteSetupCommandValidator _validator = new();

    private static CompleteSetupCommand Command(
        string username = "admin",
        string email = "admin@example.com",
        string password = "correct horse battery staple")
    {
        return new CompleteSetupCommand("token", username, email, password, "203.0.113.7", "agent");
    }

    /// <summary>A password shorter than twelve characters is rejected.</summary>
    [Fact]
    public void A_password_shorter_than_twelve_characters_is_rejected()
    {
        _validator.TestValidate(Command(password: "short")).ShouldHaveValidationErrorFor(c => c.Password);
    }

    /// <summary>A password equal to the username is rejected.</summary>
    [Fact]
    public void A_password_equal_to_the_username_is_rejected()
    {
        _validator.TestValidate(Command(username: "administrator", password: "administrator"))
            .ShouldHaveValidationErrorFor(c => c.Password);
    }

    /// <summary>A username with a space is rejected.</summary>
    [Fact]
    public void A_username_with_a_space_is_rejected()
    {
        _validator.TestValidate(Command(username: "the admin")).ShouldHaveValidationErrorFor(c => c.Username);
    }

    /// <summary>An address that is not an email is rejected.</summary>
    [Fact]
    public void An_address_that_is_not_an_email_is_rejected()
    {
        _validator.TestValidate(Command(email: "not-an-email")).ShouldHaveValidationErrorFor(c => c.Email);
    }

    /// <summary>An empty token is rejected before it reaches the handler.</summary>
    [Fact]
    public void An_empty_token_is_rejected_before_it_reaches_the_handler()
    {
        var command = new CompleteSetupCommand("", "admin", "a@b.com", "correct horse battery staple", "ip", "ua");

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.Token);
    }

    /// <summary>A display-name wrapped address is rejected, which FluentValidation's own rule accepted.</summary>
    /// <remarks>
    /// The behaviour change this module took on when the panel's e-mail rule moved to
    /// SharedKernel/Utilities/Mail: <c>.EmailAddress()</c> asked only for an "@" with something
    /// either side, so it let a display name plus an address through one field that validates
    /// neither.
    /// </remarks>
    [Theory]
    [InlineData("Ops Team <ops@example.com>")]
    [InlineData("\"Ops\" <ops@example.com>")]
    public void A_display_name_wrapped_address_is_rejected(string email)
    {
        _validator.TestValidate(Command(email: email)).ShouldHaveValidationErrorFor(c => c.Email);
    }

    /// <summary>An address carrying a control character is rejected before anything parses it.</summary>
    /// <remarks>
    /// The quoted-local-part case is the one that proves the control-character sweep is doing work:
    /// <c>MailAddress</c> parses it and round-trips it unchanged, so every other layer of the shared
    /// rule waves it through.
    /// </remarks>
    [Theory]
    [InlineData("admin@example.com\r\nBcc: attacker@example.net")]
    [InlineData("admin@example.com\0")]
    [InlineData("\"a\u0001b\"@example.com")]
    public void An_address_carrying_a_control_character_is_rejected(string email)
    {
        _validator.TestValidate(Command(email: email)).ShouldHaveValidationErrorFor(c => c.Email);
    }

    /// <summary>An address the shared rule would allow is still rejected when the column cannot hold it.</summary>
    /// <remarks>
    /// The shared rule stops at the standard's 320; the Users table stops at 254. The stricter of
    /// the two wins, and it is this module's cap because the column is this module's fact.
    /// </remarks>
    [Fact]
    public void An_address_longer_than_the_column_is_rejected_even_though_the_shared_rule_allows_it()
    {
        var email = new string('a', 248) + "@example.com";

        Assert.Equal(260, email.Length);
        Assert.True(EmailAddressRule.IsAddress(email));
        _validator.TestValidate(Command(email: email)).ShouldHaveValidationErrorFor(c => c.Email);
    }

    /// <summary>An ordinary setup passes.</summary>
    [Fact]
    public void An_ordinary_setup_passes()
    {
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }
}
