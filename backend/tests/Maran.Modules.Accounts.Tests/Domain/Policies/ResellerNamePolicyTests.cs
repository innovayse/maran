using Maran.Modules.Accounts.Domain.Policies;

namespace Maran.Modules.Accounts.Tests.Domain.Policies;

/// <summary>What <see cref="ResellerNamePolicy"/> accepts as a reseller's name.</summary>
public sealed class ResellerNamePolicyTests
{
    /// <summary>A name in the account alphabet and length is accepted.</summary>
    /// <param name="name">The name.</param>
    [Theory]
    [InlineData("acme")]
    [InlineData("acme_hosting2")]
    [InlineData("abc")]
    public void A_name_in_the_alphabet_and_length_is_accepted(string name)
    {
        Assert.True(ResellerNamePolicy.IsAcceptable(name));
    }

    /// <summary>The shortest and the longest lengths are accepted, one outside either is refused.</summary>
    [Fact]
    public void The_length_limits_are_inclusive()
    {
        Assert.True(ResellerNamePolicy.IsAcceptable(new string('a', ResellerNamePolicy.MinimumLength)));
        Assert.True(ResellerNamePolicy.IsAcceptable(new string('a', ResellerNamePolicy.MaximumLength)));
        Assert.False(ResellerNamePolicy.IsAcceptable(new string('a', ResellerNamePolicy.MinimumLength - 1)));
        Assert.False(ResellerNamePolicy.IsAcceptable(new string('a', ResellerNamePolicy.MaximumLength + 1)));
    }

    /// <summary>A name that is missing, outside the alphabet or not starting with a letter is refused.</summary>
    /// <param name="name">The name.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Acme")]
    [InlineData("1acme")]
    [InlineData("_acme")]
    [InlineData("ac-me")]
    [InlineData("ac me")]
    [InlineData("acme\n")]
    [InlineData("acmé")]
    public void A_name_outside_the_alphabet_is_refused(string? name)
    {
        Assert.False(ResellerNamePolicy.IsAcceptable(name));
    }

    /// <summary>A name the system reserves is refused even though its characters are fine.</summary>
    /// <param name="name">The reserved name.</param>
    [Theory]
    [InlineData("root")]
    [InlineData("postgres")]
    [InlineData("maran_panel")]
    public void A_reserved_name_is_refused(string name)
    {
        Assert.False(ResellerNamePolicy.IsAcceptable(name));
    }
}
