using Maran.Modules.Mail.Commands.ChangeMailboxPassword;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Commands.ChangeMailboxPassword;

/// <summary>Behavioral contract of <see cref="ChangeMailboxPasswordCommandValidator"/>.</summary>
public sealed class ChangeMailboxPasswordCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ChangeMailboxPasswordCommandValidator _validator = new();

    /// <summary>Passwords at both ends of the length range are accepted — the inverse control for the refusals.</summary>
    [Fact]
    public void Passwords_at_both_ends_of_the_length_range_are_accepted()
    {
        Assert.True(_validator.Validate(new ChangeMailboxPasswordCommand(MailboxGroupSeeds.Password)).IsValid);
        Assert.True(_validator.Validate(
            new ChangeMailboxPasswordCommand(new string('a', MailDefaults.PasswordMinLength))).IsValid);
        Assert.True(_validator.Validate(
            new ChangeMailboxPasswordCommand(new string('a', MailDefaults.PasswordMaxLength))).IsValid);
    }

    /// <summary>A password the policy refuses is refused with the weak-password code.</summary>
    /// <param name="password">The password sent.</param>
    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has a tab\tinside it")]
    [InlineData("has a line\nbreak inside")]
    public void A_password_the_policy_refuses_is_refused(string password)
    {
        var result = _validator.Validate(new ChangeMailboxPasswordCommand(password));

        Assert.Equal(MailboxGroupErrorCodes.MailPasswordTooWeak, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A password beyond the ceiling is refused.</summary>
    [Fact]
    public void A_password_beyond_the_ceiling_is_refused()
    {
        var result = _validator.Validate(
            new ChangeMailboxPasswordCommand(new string('a', MailDefaults.PasswordMaxLength + 1)));

        Assert.Equal(MailboxGroupErrorCodes.MailPasswordTooWeak, Assert.Single(result.Errors).ErrorMessage);
    }
}
