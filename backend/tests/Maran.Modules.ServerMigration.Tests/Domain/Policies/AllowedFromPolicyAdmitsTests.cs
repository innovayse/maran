using Maran.Modules.ServerMigration.Domain.Policies;

namespace Maran.Modules.ServerMigration.Tests.Domain.Policies;

/// <summary>Whether a caller's address falls inside the list a grant was tied to (issue #127).</summary>
public sealed class AllowedFromPolicyAdmitsTests
{
    /// <summary>No restriction admits any address, the placeholder for an unknown peer included.</summary>
    [Fact]
    public void No_restriction_admits_everyone()
    {
        Assert.True(AllowedFromPolicy.Admits(null, "198.51.100.7"));
        Assert.True(AllowedFromPolicy.Admits(null, ClientAddressUnknown));
    }

    /// <summary>An address inside one of the ranges is admitted, and one outside all of them is not.</summary>
    [Fact]
    public void An_address_is_admitted_only_inside_a_listed_range()
    {
        AllowedFromPolicy.TryRead("198.51.100.0/24, 203.0.113.9", out var canonical);

        Assert.True(AllowedFromPolicy.Admits(canonical, "198.51.100.200"));
        Assert.True(AllowedFromPolicy.Admits(canonical, "203.0.113.9"));
        Assert.False(AllowedFromPolicy.Admits(canonical, "198.51.101.1"));
        Assert.False(AllowedFromPolicy.Admits(canonical, "203.0.113.10"));
    }

    /// <summary>A restriction never admits a caller that could not be identified.</summary>
    [Fact]
    public void A_restriction_never_admits_an_unidentified_caller()
    {
        AllowedFromPolicy.TryRead("198.51.100.0/24", out var canonical);

        Assert.False(AllowedFromPolicy.Admits(canonical, ClientAddressUnknown));
        Assert.False(AllowedFromPolicy.Admits(canonical, string.Empty));
    }

    /// <summary>The placeholder the panel records when the peer address is unknown.</summary>
    private const string ClientAddressUnknown = "unknown";
}
