using Maran.Modules.Ssl.Domain.Policies;

namespace Maran.Modules.Ssl.Tests.Domain.Policies;

/// <summary>
/// The kind of a directory URL is an exact match on the two well-known Let's Encrypt documents and
/// nothing else, so a look-alike address is never taken for a sandbox or for the real authority.
/// </summary>
public sealed class AcmeDirectoryKindPolicyTests
{
    /// <summary>The exact production URL is production.</summary>
    [Fact]
    public void The_exact_production_url_is_production()
    {
        Assert.Equal("production", AcmeDirectoryKindPolicy.KindOf("https://acme-v02.api.letsencrypt.org/directory"));
    }

    /// <summary>The exact staging URL is staging.</summary>
    [Fact]
    public void The_exact_staging_url_is_staging()
    {
        Assert.Equal(
            "staging", AcmeDirectoryKindPolicy.KindOf("https://acme-staging-v02.api.letsencrypt.org/directory"));
    }

    /// <summary>Anything that is not an exact match is custom, however much it looks like a known one.</summary>
    /// <param name="url">The configured URL.</param>
    [Theory]
    [InlineData("https://acme.internal.example/directory")]
    [InlineData("https://staging.ca.example/directory")]
    [InlineData("https://acme-staging-v02.api.letsencrypt.org/directory/")]
    [InlineData("https://acme-staging-v02.api.letsencrypt.org/other")]
    [InlineData("https://acme-v02.api.letsencrypt.org/directory?x=1")]
    [InlineData("HTTPS://ACME-V02.API.LETSENCRYPT.ORG/DIRECTORY")]
    [InlineData("http://acme-v02.api.letsencrypt.org/directory")]
    [InlineData("")]
    public void Anything_that_is_not_an_exact_match_is_custom(string url)
    {
        Assert.Equal("custom", AcmeDirectoryKindPolicy.KindOf(url));
    }
}
