using Maran.Modules.ServerMigration.Domain.Policies;

namespace Maran.Modules.ServerMigration.Tests.Domain.Policies;

/// <summary>What the panel accepts as the address of a remote server, and the one spelling it stores.</summary>
public sealed class SourceHostPolicyTests
{
    /// <summary>The stored spelling is trimmed and lower case, so two spellings of one host are one host.</summary>
    [Fact]
    public void The_stored_spelling_is_trimmed_and_lower_case()
    {
        Assert.Equal("old.example.net", SourceHostPolicy.Normalize("  Old.Example.NET "));
    }

    /// <summary>Names with two or more labels and address literals are valid.</summary>
    /// <param name="host">The host.</param>
    [Theory]
    [InlineData("old.example.net")]
    [InlineData("a.b")]
    [InlineData("203.0.113.5")]
    [InlineData("2001:db8::1")]
    public void A_name_or_a_literal_is_valid(string host)
    {
        Assert.True(SourceHostPolicy.IsValid(host));
    }

    /// <summary>Anything else is not: a single label, a scheme, a port, a range, a scope id, an overlong name, nothing.</summary>
    /// <param name="host">The host.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("ssh://old.example.net")]
    [InlineData("old.example.net:22")]
    [InlineData("10.0.0.0/8")]
    [InlineData("fe80::1%eth0")]
    [InlineData("-bad.example.net")]
    [InlineData("old example.net")]
    public void Anything_else_is_not_valid(string? host)
    {
        Assert.False(SourceHostPolicy.IsValid(host));
    }

    /// <summary>A name longer than DNS allows is refused; one at the limit is not refused for length.</summary>
    [Fact]
    public void A_name_longer_than_dns_allows_is_refused()
    {
        var label = new string('a', 63);
        var atTheLimit = $"{label}.{label}.{label}.{new string('a', 61)}";
        var over = atTheLimit + "a";

        Assert.Equal(253, atTheLimit.Length);
        Assert.True(SourceHostPolicy.IsValid(atTheLimit));
        Assert.False(SourceHostPolicy.IsValid(over));
    }
}
