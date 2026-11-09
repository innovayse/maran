using Maran.Host.Demo.Pseudonymisation;

namespace Maran.Host.Demo.Tests.Pseudonymisation;

/// <summary>A user agent is cut to a browser family and no more.</summary>
public sealed class UserAgentFamiliesTests
{
    /// <summary>
    /// Each browser's real header maps to its family, the specific token winning over the generic one.
    /// </summary>
    [Theory]
    [InlineData(
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
        "Chrome")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0",
        "Edge")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36 OPR/114.0.0.0",
        "Opera")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:132.0) Gecko/20100101 Firefox/132.0", "Firefox")]
    [InlineData(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/17.4 Safari/605.1.15",
        "Safari")]
    [InlineData("curl/8.5.0", "curl")]
    [InlineData("SomeBot 1.0 (+https://example.test/bot)", "Other")]
    public void A_header_becomes_its_family(string header, string family)
    {
        Assert.Equal(family, UserAgentFamilies.Of(header));
    }

    /// <summary>An empty or missing header stays empty.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_header_stays_empty(string? header)
    {
        Assert.Equal(UserAgentFamilies.None, UserAgentFamilies.Of(header));
    }
}
