using Maran.SharedKernel.Utilities.Network;

namespace Maran.SharedKernel.Tests.Utilities.Network;

/// <summary>
/// What this panel accepts as a DNS host name — the one definition Sites, both Ssl commands and
/// Accounts share, so that a value refused by one of them is refused by all four.
/// </summary>
public sealed class HostNameRuleTests
{
    /// <summary>An ordinary host name of two or more labels is accepted.</summary>
    [Theory]
    [InlineData("example.com")]
    [InlineData("www.example.com")]
    [InlineData("a.b")]
    [InlineData("xn--80ak6aa92e.com")]
    [InlineData("host-1.sub-domain.example.co.uk")]
    public void An_ordinary_host_name_of_two_or_more_labels_is_accepted(string candidate)
    {
        Assert.True(HostNameRule.IsHostName(candidate));
    }

    /// <summary>A host name with a trailing newline is rejected because it would inject a config directive.</summary>
    [Theory]
    [InlineData("example.com\n")]
    [InlineData("example.com\r\n")]
    [InlineData("example.com\r")]
    [InlineData("example.com\nserver_name evil.example.com;")]
    public void A_host_name_with_a_trailing_newline_is_rejected_because_it_would_inject_a_config_directive(
        string candidate)
    {
        // The reason this rule is anchored `\A…\z` and not `^…$`: in .NET `$` also matches
        // immediately before a trailing newline, so a `$`-anchored pattern accepts the first two of
        // these — and the value is written into an nginx server_name directive
        // (rules/security.md item 4).
        Assert.False(HostNameRule.IsHostName(candidate));
    }

    /// <summary>A host name with a leading newline is rejected.</summary>
    [Fact]
    public void A_host_name_with_a_leading_newline_is_rejected()
    {
        Assert.False(HostNameRule.IsHostName("\nexample.com"));
    }

    /// <summary>A malformed host name is rejected.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("nodot")]
    [InlineData("-leading.example.com")]
    [InlineData("trailing-.example.com")]
    [InlineData("example..com")]
    [InlineData("example.com/path")]
    [InlineData("example com")]
    [InlineData("exa_mple.com")]
    public void A_malformed_host_name_is_rejected(string candidate)
    {
        Assert.False(HostNameRule.IsHostName(candidate));
    }

    /// <summary>A null candidate is rejected rather than throwing.</summary>
    [Fact]
    public void A_null_candidate_is_rejected_rather_than_throwing()
    {
        Assert.False(HostNameRule.IsHostName(null));
    }

    /// <summary>A label longer than sixty three characters is rejected.</summary>
    [Fact]
    public void A_label_longer_than_sixty_three_characters_is_rejected()
    {
        var overLongLabel = new string('a', 64);

        Assert.False(HostNameRule.IsHostName($"{overLongLabel}.com"));
    }

    /// <summary>The maximum length is the DNS ceiling that each caller states as its own rule.</summary>
    [Fact]
    public void The_maximum_length_is_the_dns_ceiling_that_each_caller_states_as_its_own_rule()
    {
        // Deliberately not enforced by IsHostName: an over-long name is reported by the caller's own
        // MaximumLength rule, so the two checks stay two checks rather than one and a decoration.
        Assert.Equal(253, HostNameRule.MaximumLength);

        var label = new string('a', 63);
        var tooLong = string.Join('.', Enumerable.Repeat(label, 4));

        Assert.True(tooLong.Length > HostNameRule.MaximumLength);
        Assert.True(HostNameRule.IsHostName(tooLong));
    }
}
