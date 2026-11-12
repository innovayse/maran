using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Services;

namespace Maran.Modules.Marketplace.Interfaces;

/// <summary>
/// What the module's commands and queries ask of the marketplace service. Every answer is a
/// <see cref="Result{T}"/>: a switched-off marketplace, an unreachable host, an unsigned or stale
/// answer, a missing entitlement and a tampered download are all coded failures, and nothing here throws
/// into the caller. Installed modules are never touched by a failure; this seam only reads and downloads.
/// </summary>
/// <remarks>
/// Failure codes, all keys of <c>ErrorMessages</c>: <c>MarketplaceDisabled</c>,
/// <c>MarketplaceHostNotConfigured</c>, <c>MarketplaceUnreachable</c>, <c>MarketplaceResponseInvalid</c>,
/// <c>MarketplaceResponseTooLarge</c>, <c>MarketplaceRedirectRefused</c>, <c>MarketplaceRequestRefused</c>,
/// <c>MarketplaceStorageUnavailable</c>, <c>CatalogueUnsigned</c>, <c>CatalogueStale</c>,
/// <c>CatalogueKeyMissing</c>, <c>LeaseRequired</c>, <c>EntitlementMissing</c>, <c>IncompatiblePanel</c>,
/// <c>PackageRevoked</c>, <c>ModuleNotInCatalogue</c>, <c>GrantHostRefused</c>, <c>DownloadSizeMismatch</c>,
/// <c>DownloadDigestMismatch</c>, <c>LicenceUnknown</c>, <c>LicenceRevoked</c>, <c>MarketplaceRateLimited</c>,
/// <c>DownloadGrantUnusable</c>.
/// </remarks>
public interface IMarketplaceClient
{
    /// <summary>Lists one page of the catalogue, from the cache while it is fresh.</summary>
    /// <param name="channel">The release channel, e.g. <c>stable</c>.</param>
    /// <param name="locale">The interface language the service localises its display text in.</param>
    /// <param name="search">A search term, or null for the whole catalogue.</param>
    /// <param name="cursor">The previous page's <see cref="CataloguePage.NextCursor"/>, or null for the first page.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page whose signature and freshness were verified, or a coded failure.</returns>
    Task<Result<CataloguePage>> ListCatalogueAsync(
        string channel,
        string locale,
        string? search,
        string? cursor,
        CancellationToken ct);

    /// <summary>Reads one module's details with every version the service offers for this panel.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <param name="locale">The interface language the service localises its display text in.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The verified details, or a coded failure (<c>ModuleNotInCatalogue</c> for an unknown id).</returns>
    Task<Result<CatalogueModuleDetails>> GetModuleAsync(string moduleId, string locale, CancellationToken ct);

    /// <summary>Asks the service which installed modules have a newer version or have been revoked.</summary>
    /// <param name="installed">The id, version and package digest of every installed module; nothing else is sent.</param>
    /// <param name="channel">The release channel.</param>
    /// <param name="locale">The interface language the service localises its display text in.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The verified report, or a coded failure.</returns>
    Task<Result<UpdatesReport>> CheckUpdatesAsync(
        IReadOnlyList<InstalledModuleReference> installed,
        string channel,
        string locale,
        CancellationToken ct);

    /// <summary>
    /// Downloads one version of a module: reads the verified catalogue entry, obtains a single-use grant,
    /// streams the package to a private temp file and checks size and digest against both the grant and
    /// the catalogue. The package's own signature is the loader's business, not this client's.
    /// </summary>
    /// <param name="moduleId">The module id.</param>
    /// <param name="version">The exact version to download.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The downloaded package, which the caller disposes (disposal deletes the file); or a coded failure,
    /// in which case no file is left behind.
    /// </returns>
    Task<Result<DownloadedPackageSource>> DownloadPackageAsync(string moduleId, string version, CancellationToken ct);

    /// <summary>Reports whether the pinned marketplace key can be read and how many keys are trusted. Makes no network call.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The key status, or <c>CatalogueKeyMissing</c> when the pinned key file is absent, unreadable or not P-384.</returns>
    Task<Result<MarketplaceKeyStatus>> GetKeyStatusAsync(CancellationToken ct);

    /// <summary>
    /// Fetches the service's signing-key list and trusts any new key in it, but only when the list is
    /// signed by a key the panel already trusts.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The key status after the refresh, or a coded failure.</returns>
    Task<Result<MarketplaceKeyStatus>> RefreshKeysAsync(CancellationToken ct);
}
