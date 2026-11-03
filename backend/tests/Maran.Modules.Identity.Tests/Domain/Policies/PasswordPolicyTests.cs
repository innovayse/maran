using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>Behavioural contract of the rules about a password a person chooses.</summary>
public sealed class PasswordPolicyTests
{
    /// <summary>A password at the minimum and one at the ceiling are both long enough.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(PasswordPolicy.MaxLength)]
    public void A_password_at_or_above_the_minimum_meets_it(int length)
    {
        Assert.True(PasswordPolicy.MeetsMinimumLength(new string('x', length), 16));
    }

    /// <summary>Null, empty and one short of the minimum do not meet it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789012345")]
    public void A_missing_or_short_password_does_not_meet_the_minimum(string? password)
    {
        Assert.False(PasswordPolicy.MeetsMinimumLength(password, 16));
    }

    /// <summary>A password at the ceiling is within it; one over is not.</summary>
    [Theory]
    [InlineData(PasswordPolicy.MaxLength, true)]
    [InlineData(PasswordPolicy.MaxLength + 1, false)]
    public void The_ceiling_admits_its_own_length_and_refuses_one_more(int length, bool expected)
    {
        Assert.Equal(expected, PasswordPolicy.IsWithinMaximumLength(new string('x', length)));
    }

    /// <summary>A password equal to the username is refused, and a different one is accepted.</summary>
    [Fact]
    public void A_password_equal_to_the_username_does_not_differ_from_it()
    {
        Assert.False(PasswordPolicy.DiffersFromUsername("administrator", "administrator"));
        Assert.True(PasswordPolicy.DiffersFromUsername("administrator1", "administrator"));
    }

    /// <summary>The comparison is ordinal: a different case is a different password.</summary>
    [Fact]
    public void The_username_comparison_is_case_sensitive()
    {
        Assert.True(PasswordPolicy.DiffersFromUsername("Administrator", "administrator"));
    }
}
