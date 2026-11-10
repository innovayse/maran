using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Tests.Domain.Policies;

/// <summary>An answer that arrived and could not be trusted is told apart from one that never arrived.</summary>
public sealed class CatalogueVerificationPolicyTests
{
    /// <summary>The four verification failures are recognised.</summary>
    [Fact]
    public void Verification_failures_are_recognised()
    {
        Assert.True(CatalogueVerificationPolicy.IsVerificationFailure(MarketplaceClientRefusals.CatalogueUnsigned));
        Assert.True(CatalogueVerificationPolicy.IsVerificationFailure(MarketplaceClientRefusals.CatalogueStale));
        Assert.True(CatalogueVerificationPolicy.IsVerificationFailure(MarketplaceClientRefusals.CatalogueKeyMissing));
        Assert.True(CatalogueVerificationPolicy.IsVerificationFailure(MarketplaceClientRefusals.ResponseInvalid));
    }

    /// <summary>A switched-off marketplace, an outage, a refused entitlement and an unknown module are requests failing, not unverified answers.</summary>
    [Fact]
    public void Other_failures_are_not_verification_failures()
    {
        Assert.False(CatalogueVerificationPolicy.IsVerificationFailure(MarketplaceClientRefusals.Disabled));
        Assert.False(CatalogueVerificationPolicy.IsVerificationFailure(MarketplaceClientRefusals.Unreachable));
        Assert.False(CatalogueVerificationPolicy.IsVerificationFailure(MarketplaceClientRefusals.EntitlementMissing));
        Assert.False(CatalogueVerificationPolicy.IsVerificationFailure(MarketplaceClientRefusals.ModuleNotInCatalogue));
    }
}
