using Maran.Modules.Marketplace.Resources;

namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// The coded reasons the marketplace client turns a request away, as ready-made <see cref="Error"/>
/// values. One file decides each code's KIND, so a code raised from the transport, the checker and the
/// downloader cannot carry three different statuses.
/// </summary>
/// <remarks>
/// Each code is a key of <c>Resources/ErrorMessages.resx</c> (en, ru, hy), so a refusal renders as a
/// sentence in the administrator's language and never carries a URL, a grant id or exception text.
/// </remarks>
public static class MarketplaceClientRefusals
{
    /// <summary>The marketplace is switched off, so nothing was sent.</summary>
    public static readonly Error Disabled =
        Error.Of(nameof(ErrorMessages.MarketplaceDisabled), ErrorType.Unavailable);

    /// <summary>The configured address cannot be used (empty, not https, or otherwise invalid).</summary>
    public static readonly Error HostNotConfigured =
        Error.Of(nameof(ErrorMessages.MarketplaceHostNotConfigured), ErrorType.Failure);

    /// <summary>Nothing answered: the host is unreachable, timed out, or answered with a server failure.</summary>
    public static readonly Error Unreachable =
        Error.Of(nameof(ErrorMessages.MarketplaceUnreachable), ErrorType.Unavailable);

    /// <summary>The answer is not the documented shape (malformed, a member missing, an unknown word, or about another module).</summary>
    public static readonly Error ResponseInvalid =
        Error.Of(nameof(ErrorMessages.MarketplaceResponseInvalid), ErrorType.UpstreamRefused);

    /// <summary>The answer is larger than the configured cap allows.</summary>
    public static readonly Error ResponseTooLarge =
        Error.Of(nameof(ErrorMessages.MarketplaceResponseTooLarge), ErrorType.UpstreamRefused);

    /// <summary>The host tried to send the panel to another host, another scheme, or past the redirect limit.</summary>
    public static readonly Error RedirectRefused =
        Error.Of(nameof(ErrorMessages.MarketplaceRedirectRefused), ErrorType.UpstreamRefused);

    /// <summary>The host refused the request with a status the panel has no more specific behaviour for.</summary>
    public static readonly Error RequestRefused =
        Error.Of(nameof(ErrorMessages.MarketplaceRequestRefused), ErrorType.UpstreamRefused);

    /// <summary>The answer carries no signature, or none a trusted key made.</summary>
    public static readonly Error CatalogueUnsigned =
        Error.Of(nameof(ErrorMessages.CatalogueUnsigned), ErrorType.UpstreamRefused);

    /// <summary>The answer is correctly signed but older than the panel accepts, or past its own validity.</summary>
    public static readonly Error CatalogueStale =
        Error.Of(nameof(ErrorMessages.CatalogueStale), ErrorType.UpstreamRefused);

    /// <summary>The pinned key file is absent, unreadable or not an ECDSA P-384 public key, so nothing can be verified.</summary>
    public static readonly Error CatalogueKeyMissing =
        Error.Of(nameof(ErrorMessages.CatalogueKeyMissing), ErrorType.Failure);

    /// <summary>The request needs a licence lease and the panel holds none that can be used.</summary>
    public static readonly Error LeaseRequired =
        Error.Of(nameof(ErrorMessages.LeaseRequired), ErrorType.Forbidden);

    /// <summary>The licence holds no entitlement to the module.</summary>
    public static readonly Error EntitlementMissing =
        Error.Of(nameof(ErrorMessages.EntitlementMissing), ErrorType.Forbidden);

    /// <summary>The module version does not run on this panel version.</summary>
    public static readonly Error IncompatiblePanel =
        Error.Of(nameof(ErrorMessages.IncompatiblePanel), ErrorType.Conflict);

    /// <summary>The module or version has been revoked by the service.</summary>
    public static readonly Error PackageRevoked =
        Error.Of(nameof(ErrorMessages.PackageRevoked), ErrorType.Conflict);

    /// <summary>The package directory cannot be created or written (permissions, a full disk).</summary>
    public static readonly Error StorageUnavailable =
        Error.Of(nameof(ErrorMessages.MarketplaceStorageUnavailable), ErrorType.Failure);

    /// <summary>The catalogue has no such module or version.</summary>
    public static readonly Error ModuleNotInCatalogue =
        Error.Of(nameof(ErrorMessages.ModuleNotInCatalogue), ErrorType.NotFound);

    /// <summary>A download address is on a host other than the configured one.</summary>
    public static readonly Error GrantHostRefused =
        Error.Of(nameof(ErrorMessages.GrantHostRefused), ErrorType.UpstreamRefused);

    /// <summary>A package is not the size the grant and the catalogue declared, or exceeds what the panel accepts.</summary>
    public static readonly Error DownloadSizeMismatch =
        Error.Of(nameof(ErrorMessages.DownloadSizeMismatch), ErrorType.UpstreamRefused);

    /// <summary>A package's SHA-256 is not the digest the grant and the catalogue declared.</summary>
    public static readonly Error DownloadDigestMismatch =
        Error.Of(nameof(ErrorMessages.DownloadDigestMismatch), ErrorType.UpstreamRefused);
}
