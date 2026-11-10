using System.Net;
using Maran.Host.IntegrationTests.Fixtures;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// Every route of the marketplace controller, written out by hand, with the call that exercises it and the
/// answer an administrator gets while the marketplace is switched off. The table is checked against the
/// controller's own attributes, so a route added without a row here fails a test by name.
/// </summary>
public static class MarketplaceRoutes
{
    /// <summary>The standing body of a mutating call: version, acknowledgement and the right password.</summary>
    private static readonly object InstallBody = new
    {
        version = MarketplaceWorld.ModuleVersion,
        acknowledged = true,
        password = EndpointHarness.Password,
    };

    /// <summary>The standing body of a call that asks only for the password.</summary>
    private static readonly object PasswordBody = new { password = EndpointHarness.Password };

    /// <summary>The eleven routes, in the order the contract lists them.</summary>
    public static IReadOnlyList<MarketplaceRoute> All { get; } =
    [
        new("GET", "status", "status", null, HttpStatusCode.OK, null),
        new(
            "PUT",
            "settings",
            "settings",
            new { enabled = false, password = EndpointHarness.Password },
            HttpStatusCode.OK,
            null),
        new("GET", "catalogue", "catalogue", null, HttpStatusCode.ServiceUnavailable, "MarketplaceDisabled"),
        new(
            "GET",
            "modules/{id}",
            "modules/" + MarketplaceWorld.ModuleId,
            null,
            HttpStatusCode.ServiceUnavailable,
            "MarketplaceDisabled"),
        new("GET", "installed", "installed", null, HttpStatusCode.OK, null),
        new("GET", "updates", "updates", null, HttpStatusCode.ServiceUnavailable, "MarketplaceDisabled"),
        new(
            "POST",
            "modules/{id}/install",
            "modules/" + MarketplaceWorld.ModuleId + "/install",
            InstallBody,
            HttpStatusCode.ServiceUnavailable,
            "MarketplaceDisabled"),
        new(
            "POST",
            "modules/{id}/update",
            "modules/" + MarketplaceWorld.ModuleId + "/update",
            InstallBody,
            HttpStatusCode.ServiceUnavailable,
            "MarketplaceDisabled"),
        new(
            "POST",
            "modules/{id}/remove",
            "modules/" + MarketplaceWorld.ModuleId + "/remove",
            PasswordBody,
            HttpStatusCode.NotFound,
            "ModuleNotInstalled"),
        new(
            "POST",
            "modules/{id}/rollback",
            "modules/" + MarketplaceWorld.ModuleId + "/rollback",
            PasswordBody,
            HttpStatusCode.NotFound,
            "ModuleNotInstalled"),
        new(
            "POST",
            "modules/{id}/restart",
            "modules/" + MarketplaceWorld.ModuleId + "/restart",
            new { },
            HttpStatusCode.NotFound,
            "ModuleNotInstalled"),
    ];
}
