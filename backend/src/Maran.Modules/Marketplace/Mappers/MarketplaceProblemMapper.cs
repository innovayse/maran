using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Mappers;

/// <summary>
/// Turns the service's failure answer into the panel's coded failure. The service's stable
/// <c>code</c> picks the behaviour; its sentence is never read, because the panel renders its own
/// localised text for the code it selects.
/// </summary>
public static class MarketplaceProblemMapper
{
    /// <summary>Maps a non-success answer.</summary>
    /// <param name="statusCode">The HTTP status.</param>
    /// <param name="body">The (capped) response body, which may or may not be a problem document.</param>
    /// <returns>The coded failure.</returns>
    public static Error ToError(int statusCode, ReadOnlySpan<byte> body)
    {
        var problem = MarketplaceJson.TryParse<ServiceProblem>(body);

        return problem?.Code switch
        {
            "entitlement_missing" => MarketplaceClientRefusals.EntitlementMissing,
            "lease_expired" => MarketplaceClientRefusals.LeaseRequired,
            "incompatible_panel" => MarketplaceClientRefusals.IncompatiblePanel,
            "revoked" => MarketplaceClientRefusals.PackageRevoked,
            "not_found" => MarketplaceClientRefusals.ModuleNotInCatalogue,
            _ => FromStatus(statusCode),
        };
    }

    /// <summary>Maps by status when the body carried no code the panel knows.</summary>
    /// <param name="statusCode">The HTTP status.</param>
    /// <returns>The coded failure: a server-side trouble reads as unreachable, anything else as a refused answer.</returns>
    private static Error FromStatus(int statusCode)
    {
        return statusCode switch
        {
            401 => MarketplaceClientRefusals.LeaseRequired,
            404 => MarketplaceClientRefusals.ModuleNotInCatalogue,
            408 or 429 or >= 500 => MarketplaceClientRefusals.Unreachable,
            _ => MarketplaceClientRefusals.RequestRefused,
        };
    }
}
