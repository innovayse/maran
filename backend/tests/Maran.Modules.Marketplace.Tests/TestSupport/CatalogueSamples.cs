using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>Builders for the catalogue values the commands and queries read.</summary>
public static class CatalogueSamples
{
    /// <summary>The digest every sample version carries.</summary>
    public const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>Builds one published version.</summary>
    /// <param name="version">The version.</param>
    /// <param name="minPanelVersion">The oldest panel version it runs on.</param>
    /// <param name="maxPanelVersion">The newest panel version it runs on, or null.</param>
    /// <param name="revoked">Whether the service withdrew it.</param>
    /// <returns>The version.</returns>
    public static CatalogueVersion Version(
        string version,
        string minPanelVersion = "1.0.0",
        string? maxPanelVersion = null,
        bool revoked = false)
    {
        return new CatalogueVersion(
            version,
            minPanelVersion,
            SizeBytes: 1024,
            Digest,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            "Notes",
            maxPanelVersion,
            revoked,
            revoked ? "Withdrawn" : null);
    }

    /// <summary>Builds one catalogue module whose newest version is <paramref name="latest"/>.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="latest">The newest version, or null for none.</param>
    /// <param name="entitlement">The entitlement state.</param>
    /// <param name="revoked">Whether the service withdrew the whole module.</param>
    /// <returns>The module.</returns>
    public static CatalogueModule Module(
        string id,
        CatalogueVersion? latest,
        EntitlementState entitlement = EntitlementState.Entitled,
        bool revoked = false)
    {
        return new CatalogueModule(
            id,
            id,
            "Summary",
            "Innovayse",
            "AddOn",
            new CatalogueEntitlement(entitlement),
            revoked,
            latest,
            new CataloguePrice("10 USD", "https://example.test/buy"));
    }

    /// <summary>Builds the details of one module with the given versions.</summary>
    /// <param name="module">The module.</param>
    /// <param name="versions">Every version on offer.</param>
    /// <returns>The details.</returns>
    public static CatalogueModuleDetails Details(CatalogueModule module, params CatalogueVersion[] versions)
    {
        var signed = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        return new CatalogueModuleDetails(module, versions, signed, signed.AddHours(24));
    }

    /// <summary>Builds a one-page catalogue.</summary>
    /// <param name="modules">The modules on the page.</param>
    /// <returns>The page.</returns>
    public static CataloguePage Page(params CatalogueModule[] modules)
    {
        var signed = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        return new CataloguePage(modules, signed, signed.AddHours(24));
    }
}
