using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>Behavioural contract of the rules about a password a person chooses.</summary>
public sealed class PasswordPolicyTests
{
    /// <summary>A password at the minimum and one at the ceiling are both acceptable.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(PasswordPolicy.MaxLength)]
    public void A_password_between_the_minimum_and_the_ceiling_has_an_acceptable_length(int length)
    {
        Assert.True(PasswordPolicy.HasAcceptableLength(new string('x', length), 16));
    }

    /// <summary>Null, empty, one short of the minimum and one over the ceiling are all refused.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_password_has_no_acceptable_length(string? password)
    {
        Assert.False(PasswordPolicy.HasAcceptableLength(password, 16));
    }

    /// <summary>One character short of the minimum, and one over the ceiling, are refused.</summary>
    [Theory]
    [InlineData(15)]
    [InlineData(PasswordPolicy.MaxLength + 1)]
    public void A_password_outside_the_bounds_has_no_acceptable_length(int length)
    {
        Assert.False(PasswordPolicy.HasAcceptableLength(new string('x', length), 16));
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
