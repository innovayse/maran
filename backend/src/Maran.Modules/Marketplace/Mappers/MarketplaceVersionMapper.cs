using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Mappers;

/// <summary>Restates a published version, with the decision already made about it, as the wire shape.</summary>
public static class MarketplaceVersionMapper
{
    /// <summary>Maps one version.</summary>
    /// <param name="version">The published version.</param>
    /// <param name="installable">Whether it can be installed now, decided by <c>InstallEligibility</c>.</param>
    /// <param name="notInstallableReason">Why not, in the caller's language; null when it can.</param>
    /// <returns>The wire shape.</returns>
    public static MarketplaceVersionDto ToDto(CatalogueVersion version, bool installable, string? notInstallableReason)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new MarketplaceVersionDto(
            version.Version,
            version.MinPanelVersion,
            version.MaxPanelVersion,
            version.SizeBytes,
            version.PublishedAt,
            version.ReleaseNotes,
            version.Revoked,
            version.RevokedReason,
            installable,
            notInstallableReason);
    }
}
