using Maran.Modules.Marketplace.Mappers;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Mappers;

/// <summary>The service's problem codes select the panel's behaviour, and a status alone is read only when the code says nothing.</summary>
public sealed class MarketplaceProblemMapperTests
{
    /// <summary>A problem document with a code.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The body bytes.</returns>
    private static byte[] Problem(string code)
    {
        return MarketplaceTestWorld.BytesOf(new { code, title = "text the panel ignores" });
    }

    /// <summary>Each known code maps to its behaviour whatever the status says.</summary>
    [Theory]
    [InlineData("entitlement_missing", "EntitlementMissing")]
    [InlineData("lease_expired", "LeaseRequired")]
    [InlineData("incompatible_panel", "IncompatiblePanel")]
    [InlineData("revoked", "PackageRevoked")]
    [InlineData("not_found", "ModuleNotInCatalogue")]
    [InlineData("licence_unknown", "LicenceUnknown")]
    [InlineData("licence_revoked", "LicenceRevoked")]
    [InlineData("rate_limited", "MarketplaceRateLimited")]
    public void A_known_code_selects_its_behaviour(string code, string expected)
    {
        Assert.Equal(expected, MarketplaceProblemMapper.ToError(400, Problem(code)).Code);
        Assert.Equal(expected, MarketplaceProblemMapper.ToError(500, Problem(code)).Code);
    }

    /// <summary>An unknown code or no body falls back to the status: server trouble is unreachable, anything else a refused answer.</summary>
    [Theory]
    [InlineData(500, "MarketplaceUnreachable")]
    [InlineData(503, "MarketplaceUnreachable")]
    [InlineData(429, "MarketplaceRateLimited")]
    [InlineData(408, "MarketplaceUnreachable")]
    [InlineData(401, "LeaseRequired")]
    [InlineData(404, "ModuleNotInCatalogue")]
    [InlineData(400, "MarketplaceRequestRefused")]
    [InlineData(302, "MarketplaceRequestRefused")]
    public void An_unknown_code_falls_back_to_the_status(int status, string expected)
    {
        Assert.Equal(expected, MarketplaceProblemMapper.ToError(status, Problem("something_new")).Code);
        Assert.Equal(expected, MarketplaceProblemMapper.ToError(status, []).Code);
        Assert.Equal(expected, MarketplaceProblemMapper.ToError(status, "<html>"u8).Code);
    }

    /// <summary>
    /// A licence the service no longer knows is not "module not in catalogue" even though it answers 404, and a
    /// limit is not "unreachable" even though it is retried like trouble; the plain 404 beside it keeps its meaning.
    /// </summary>
    [Fact]
    public void The_licence_and_limit_codes_are_not_read_by_their_status()
    {
        Assert.Equal("LicenceUnknown", MarketplaceProblemMapper.ToError(404, Problem("licence_unknown")).Code);
        Assert.Equal("ModuleNotInCatalogue", MarketplaceProblemMapper.ToError(404, Problem("not_found")).Code);
        Assert.Equal("LicenceRevoked", MarketplaceProblemMapper.ToError(403, Problem("licence_revoked")).Code);
        Assert.Equal("EntitlementMissing", MarketplaceProblemMapper.ToError(403, Problem("entitlement_missing")).Code);
        Assert.Equal("MarketplaceRateLimited", MarketplaceProblemMapper.ToError(429, Problem("rate_limited")).Code);
        Assert.Equal("MarketplaceUnreachable", MarketplaceProblemMapper.ToError(503, Problem("rate_limited_not")).Code);
    }

    /// <summary>On a download the address is the only credential, so a missing or gone answer is about the grant; other answers read as usual.</summary>
    [Theory]
    [InlineData(404, "not_found", "DownloadGrantUnusable")]
    [InlineData(404, null, "DownloadGrantUnusable")]
    [InlineData(410, null, "DownloadGrantUnusable")]
    [InlineData(410, "revoked", "PackageRevoked")]
    [InlineData(429, "rate_limited", "MarketplaceRateLimited")]
    [InlineData(503, "package_unavailable", "MarketplaceUnreachable")]
    [InlineData(400, null, "MarketplaceRequestRefused")]
    public void A_download_failure_names_the_grant_not_the_catalogue(int status, string? code, string expected)
    {
        var body = code is null ? [] : Problem(code);

        Assert.Equal(expected, MarketplaceProblemMapper.ToDownloadError(status, body).Code);
    }

    /// <summary>The kinds are decided in one place: entitlement and lease refusals are forbidden, a revoked package a conflict.</summary>
    [Fact]
    public void The_refusals_carry_the_kinds_decided_for_them()
    {
        Assert.Equal(ErrorType.Forbidden, MarketplaceClientRefusals.EntitlementMissing.Type);
        Assert.Equal(ErrorType.Forbidden, MarketplaceClientRefusals.LeaseRequired.Type);
        Assert.Equal(ErrorType.Conflict, MarketplaceClientRefusals.PackageRevoked.Type);
        Assert.Equal(ErrorType.NotFound, MarketplaceClientRefusals.ModuleNotInCatalogue.Type);
        Assert.Equal(ErrorType.Unavailable, MarketplaceClientRefusals.Disabled.Type);
        Assert.Equal(ErrorType.Forbidden, MarketplaceClientRefusals.LicenceUnknown.Type);
        Assert.Equal(ErrorType.Forbidden, MarketplaceClientRefusals.LicenceRevoked.Type);
        Assert.Equal(ErrorType.Unavailable, MarketplaceClientRefusals.RateLimited.Type);
        Assert.Equal(ErrorType.UpstreamRefused, MarketplaceClientRefusals.DownloadGrantUnusable.Type);
    }
}
