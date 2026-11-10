using Maran.Modules.ServerMigration.Domain.Policies;

namespace Maran.Modules.ServerMigration.Tests.Domain.Policies;

/// <summary>What the panel accepts as the login on a remote server.</summary>
public sealed class SourceUserPolicyTests
{
    /// <summary>A login is kept as typed apart from surrounding space: logins are case sensitive.</summary>
    [Fact]
    public void A_login_is_trimmed_and_not_lowered()
    {
        Assert.Equal("Root", SourceUserPolicy.Normalize(" Root "));
    }

    /// <summary>The shape of a Linux system user name is valid, at the longest and the shortest.</summary>
    /// <param name="user">The login.</param>
    [Theory]
    [InlineData("a")]
    [InlineData("_")]
    [InlineData("maran-mig_1")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdef")]
    public void A_system_user_name_is_valid(string user)
    {
        Assert.True(SourceUserPolicy.IsValid(user));
    }

    /// <summary>Anything else is not.</summary>
    /// <param name="user">The login.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Root")]
    [InlineData("0day")]
    [InlineData("-x")]
    [InlineData("a.b")]
    [InlineData("a b")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")]
    public void Anything_else_is_not_valid(string? user)
    {
        Assert.False(SourceUserPolicy.IsValid(user));
    }
}
