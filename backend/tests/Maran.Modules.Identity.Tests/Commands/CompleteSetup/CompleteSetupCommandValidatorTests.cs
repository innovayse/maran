using FluentValidation.TestHelper;
using Maran.Modules.Identity.Commands.CompleteSetup;

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

    /// <summary>An ordinary setup passes.</summary>
    [Fact]
    public void An_ordinary_setup_passes()
    {
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }
}
