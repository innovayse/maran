using Maran.SharedKernel.Utilities.Network;

namespace Maran.SharedKernel.Tests.Utilities.Network;

/// <summary>
/// Which host names no tenant may claim whatever their shape: address literals, the loopback
/// family and special-use suffixes. The static half of the reserved set, shared by Sites, Ssl and
/// Ftp so that a name refused by one is refused by all.
/// </summary>
public sealed class ReservedHostNameRuleTests
{
    /// <summary>A name whose final label is all digits reads as an address and is reserved.</summary>
    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.3")]
    [InlineData("10.1")]
    [InlineData("12345")]
    [InlineData("shop.123")]
    [InlineData("example.007")]
    public void A_name_with_a_numeric_final_label_is_reserved(string candidate)
    {
        Assert.True(ReservedHostNameRule.IsReserved(candidate));
    }

    /// <summary>A final label containing a letter is an ordinary name, however many digits it holds.</summary>
    [Theory]
    [InlineData("abc1.com")]
    [InlineData("shop.1a")]
    [InlineData("a.b2")]
    [InlineData("web2.example.com")]
    [InlineData("1.2.3.com")]
    public void A_final_label_with_a_letter_is_not_reserved(string candidate)
    {
        Assert.False(ReservedHostNameRule.IsReserved(candidate));
    }

    /// <summary>The loopback name, bare and as a parent of any label, is reserved.</summary>
    [Theory]
    [InlineData("localhost")]
    [InlineData("foo.localhost")]
    [InlineData("a.b.localhost")]
    public void Localhost_and_its_subdomains_are_reserved(string candidate)
    {
        Assert.True(ReservedHostNameRule.IsReserved(candidate));
    }

    /// <summary>Each special-use suffix is reserved.</summary>
    [Theory]
    [InlineData("x.local")]
    [InlineData("x.localdomain")]
    [InlineData("x.internal")]
    [InlineData("x.lan")]
    [InlineData("x.home.arpa")]
    [InlineData("home.arpa")]
    [InlineData("x.invalid")]
    public void A_special_use_suffix_is_reserved(string candidate)
    {
        Assert.True(ReservedHostNameRule.IsReserved(candidate));
    }

    /// <summary>An ordinary name, including ones that merely contain a reserved word, is not reserved.</summary>
    [Theory]
    [InlineData("shop.example.com")]
    [InlineData("shop.example.test")]
    [InlineData("shop.example")]
    [InlineData("localhost.example.com")]
    [InlineData("internal.example.com")]
    [InlineData("mylocal.com")]
    [InlineData("notlocalhost.com")]
    [InlineData("xlocal")]
    public void An_ordinary_name_is_not_reserved(string candidate)
    {
        Assert.False(ReservedHostNameRule.IsReserved(candidate));
    }

    /// <summary>Reservation ignores letter case.</summary>
    [Theory]
    [InlineData("FOO.LOCAL")]
    [InlineData("LocalHost")]
    [InlineData("A.LOCALHOST")]
    [InlineData("X.Home.Arpa")]
    public void Reservation_ignores_case(string candidate)
    {
        Assert.True(ReservedHostNameRule.IsReserved(candidate));
    }

    /// <summary>One trailing dot does not turn a reserved name into a free one.</summary>
    [Theory]
    [InlineData("203.0.113.7.")]
    [InlineData("foo.localhost.")]
    [InlineData("x.lan.")]
    public void A_single_trailing_dot_does_not_escape_reservation(string candidate)
    {
        Assert.True(ReservedHostNameRule.IsReserved(candidate));
    }

    /// <summary>A trailing dot on an ordinary name leaves it unreserved.</summary>
    [Fact]
    public void A_trailing_dot_on_an_ordinary_name_is_not_reserved()
    {
        Assert.False(ReservedHostNameRule.IsReserved("shop.example.com."));
    }

    /// <summary>Null, empty and a lone dot are the shape rule's concern and are not reserved.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    public void A_null_or_empty_candidate_is_not_reserved_and_does_not_throw(string? candidate)
    {
        Assert.False(ReservedHostNameRule.IsReserved(candidate));
    }
}
