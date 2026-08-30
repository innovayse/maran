using FluentValidation.TestHelper;
using Maran.Modules.Identity.Commands.Login;

namespace Maran.Modules.Identity.Tests.Commands.Login;
/// <summary>Behavioural contract of login command validator.</summary>

public sealed class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    private static LoginCommand Command(string username = "admin", string password = "a-password")
    {
        return new LoginCommand(username, password, "203.0.113.7", "agent");
    }

    /// <summary>An empty username is rejected.</summary>
    [Fact]
    public void An_empty_username_is_rejected()
    {
        _validator.TestValidate(Command(username: "")).ShouldHaveValidationErrorFor(c => c.Username);
    }

    /// <summary>An empty password is rejected.</summary>
    [Fact]
    public void An_empty_password_is_rejected()
    {
        _validator.TestValidate(Command(password: "")).ShouldHaveValidationErrorFor(c => c.Password);
    }

    /// <summary>A password longer than the cap is rejected before it reaches the hasher.</summary>
    [Fact]
    public void A_password_longer_than_the_cap_is_rejected_before_it_reaches_the_hasher()
    {
        // The cap is a denial-of-service guard, not a policy: Argon2id deliberately costs memory
        // and time, so an unbounded password is an unbounded amount of both, per attempt.
        _validator.TestValidate(Command(password: new string('x', 257))).ShouldHaveValidationErrorFor(c => c.Password);
    }

    /// <summary>A password at the cap is accepted.</summary>
    [Fact]
    public void A_password_at_the_cap_is_accepted()
    {
        _validator.TestValidate(Command(password: new string('x', 256))).ShouldNotHaveValidationErrorFor(c => c.Password);
    }

    /// <summary>An ordinary attempt passes.</summary>
    [Fact]
    public void An_ordinary_attempt_passes()
    {
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }
}
