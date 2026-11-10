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
    public void A_known_code_selects_its_behaviour(string code, string expected)
    {
        Assert.Equal(expected, MarketplaceProblemMapper.ToError(400, Problem(code)).Code);
        Assert.Equal(expected, MarketplaceProblemMapper.ToError(500, Problem(code)).Code);
    }

    /// <summary>An unknown code or no body falls back to the status: server trouble is unreachable, anything else a refused answer.</summary>
    [Theory]
    [InlineData(500, "MarketplaceUnreachable")]
    [InlineData(503, "MarketplaceUnreachable")]
    [InlineData(429, "MarketplaceUnreachable")]
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

    /// <summary>The kinds are decided in one place: entitlement and lease refusals are forbidden, a revoked package a conflict.</summary>
    [Fact]
    public void The_refusals_carry_the_kinds_decided_for_them()
    {
        Assert.Equal(ErrorType.Forbidden, MarketplaceClientRefusals.EntitlementMissing.Type);
        Assert.Equal(ErrorType.Forbidden, MarketplaceClientRefusals.LeaseRequired.Type);
        Assert.Equal(ErrorType.Conflict, MarketplaceClientRefusals.PackageRevoked.Type);
        Assert.Equal(ErrorType.NotFound, MarketplaceClientRefusals.ModuleNotInCatalogue.Type);
        Assert.Equal(ErrorType.Unavailable, MarketplaceClientRefusals.Disabled.Type);
    }
}
