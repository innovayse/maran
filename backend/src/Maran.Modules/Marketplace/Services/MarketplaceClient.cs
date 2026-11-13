using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Mappers;
using Maran.Modules.Marketplace.Models;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// The marketplace as the commands and queries use it: catalogue, details, updates, download. It owns the
/// order of the checks (switch, cache, request, signature, freshness, then the parts of the answer that
/// matter) and none of the mechanics, which belong to the transport, the checker and the downloader.
/// </summary>
/// <remarks>
/// <para>
/// <b>The switch comes first.</b> Every call asks <see cref="IMarketplaceSwitch"/> before anything else
/// and, when it says off, goes through <see cref="DisabledMarketplaceTransport"/>, which answers
/// <c>MarketplaceDisabled</c> and has no way to reach the network. The cache is not consulted while off,
/// so switching the marketplace off hides what was fetched while it was on.
/// </para>
/// <para>
/// <b>Nothing here changes an installed module.</b> A failure of any kind returns a coded
/// <see cref="Result"/>; the modules that already run are not touched by it.
/// </para>
/// </remarks>
public sealed class MarketplaceClient : IMarketplaceClient
{
    /// <summary>The path prefix of the service's API.</summary>
    private const string ApiPrefix = "/api/v1";

    /// <summary>The language used for requests whose answer is not shown (a download needs no translation).</summary>
    private const string NeutralLocale = "en";

    /// <summary>The stored on/off decision.</summary>
    private readonly IMarketplaceSwitch _switch;

    /// <summary>The transport used while the marketplace is on.</summary>
    private readonly MarketplaceHttpTransport _http;

    /// <summary>The transport used while it is off.</summary>
    private readonly DisabledMarketplaceTransport _disabled;

    /// <summary>Verifies and parses answers.</summary>
    private readonly SignedResponseReader _reader;

    /// <summary>Verifies signatures and freshness, and holds the trusted keys.</summary>
    private readonly CatalogueSignatureChecker _checker;

    /// <summary>Holds verified catalogue pages.</summary>
    private readonly CatalogueCache _cache;

    /// <summary>Names the lease credential a catalogue page is asked with.</summary>
    private readonly LeaseFingerprintReader _lease;

    /// <summary>Downloads and checks a package.</summary>
    private readonly PackageDownloader _downloader;

    /// <summary>Creates the client.</summary>
    /// <param name="marketplaceSwitch">The stored on/off decision.</param>
    /// <param name="http">The transport used while the marketplace is on.</param>
    /// <param name="disabled">The transport used while it is off.</param>
    /// <param name="reader">Verifies and parses answers.</param>
    /// <param name="checker">Verifies signatures and freshness, and holds the trusted keys.</param>
    /// <param name="cache">Holds verified catalogue pages.</param>
    /// <param name="lease">Names the lease credential a catalogue page is asked with.</param>
    /// <param name="downloader">Downloads and checks a package.</param>
    public MarketplaceClient(
        IMarketplaceSwitch marketplaceSwitch,
        MarketplaceHttpTransport http,
        DisabledMarketplaceTransport disabled,
        SignedResponseReader reader,
        CatalogueSignatureChecker checker,
        CatalogueCache cache,
        LeaseFingerprintReader lease,
        PackageDownloader downloader)
    {
        _switch = marketplaceSwitch;
        _http = http;
        _disabled = disabled;
        _reader = reader;
        _checker = checker;
        _cache = cache;
        _lease = lease;
        _downloader = downloader;
    }

    /// <inheritdoc />
    public async Task<Result<CataloguePage>> ListCatalogueAsync(
        string channel,
        string locale,
        string? search,
        string? cursor,
        CancellationToken ct)
    {
        var enabled = await _switch.IsEnabledAsync(ct);
        var transport = Choose(enabled);

        // The switch is off: nothing is read, not even the lease, and nothing is served from the cache.
        var key = CatalogueCache.KeyOf(channel, locale, search, cursor, enabled ? await _lease.ReadAsync(ct) : string.Empty);
        var cached = enabled ? _cache.Find(key) : null;

        // A fresh entry is served without a request, but its signature's age is judged again every time.
        if (cached is not null && _cache.IsFresh(cached) && _checker.CheckFreshness(cached.Page).IsSuccess)
        {
            return Result<CataloguePage>.Ok(cached.Page);
        }

        var path = $"{ApiPrefix}/catalogue?channel={Uri.EscapeDataString(channel)}"
            + $"&locale={Uri.EscapeDataString(locale)}"
            + (string.IsNullOrEmpty(search) ? string.Empty : $"&search={Uri.EscapeDataString(search)}")
            + (string.IsNullOrEmpty(cursor) ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");

        var answer = await transport.SendAsync(
            new MarketplaceRequest(
                HttpMethod.Get, path, locale, LeaseRequirement.Optional, ResponseLimit.Catalogue,
                IfNoneMatch: cached?.ETag),
            ct);
        if (!answer.IsSuccess)
        {
            return Result<CataloguePage>.Fail(answer.Error!);
        }

        if (answer.Value.StatusCode == 304 && cached is not null)
        {
            return Revalidated(key, cached);
        }

        var page = await _reader.ReadDocumentAsync<CataloguePage>(answer.Value, ct);
        if (page.IsSuccess)
        {
            _cache.Store(key, page.Value, answer.Value.ETag);
        }

        return page;
    }

    /// <inheritdoc />
    public async Task<Result<CatalogueModuleDetails>> GetModuleAsync(
        string moduleId,
        string locale,
        CancellationToken ct)
    {
        var transport = Choose(await _switch.IsEnabledAsync(ct));
        var path = $"{ApiPrefix}/modules/{Uri.EscapeDataString(moduleId)}?locale={Uri.EscapeDataString(locale)}";

        var answer = await transport.SendAsync(
            new MarketplaceRequest(HttpMethod.Get, path, locale, LeaseRequirement.Optional, ResponseLimit.Catalogue),
            ct);
        if (!answer.IsSuccess)
        {
            return Result<CatalogueModuleDetails>.Fail(answer.Error!);
        }

        var details = await _reader.ReadDocumentAsync<CatalogueModuleDetails>(answer.Value, ct);

        // A signed answer about another module is a substitution, not a result.
        return details.IsSuccess && !string.Equals(details.Value.Module.Id, moduleId, StringComparison.Ordinal)
            ? Result<CatalogueModuleDetails>.Fail(MarketplaceClientRefusals.ResponseInvalid)
            : details;
    }

    /// <inheritdoc />
    public async Task<Result<UpdatesReport>> CheckUpdatesAsync(
        IReadOnlyList<InstalledModuleReference> installed,
        string channel,
        string locale,
        CancellationToken ct)
    {
        var transport = Choose(await _switch.IsEnabledAsync(ct));
        var body = MarketplaceJson.Serialize(new UpdatesRequest(channel, installed));

        var answer = await transport.SendAsync(
            new MarketplaceRequest(
                HttpMethod.Post, $"{ApiPrefix}/updates", locale, LeaseRequirement.Optional, ResponseLimit.Catalogue,
                Body: body),
            ct);

        return answer.IsSuccess
            ? await _reader.ReadDocumentAsync<UpdatesReport>(answer.Value, ct)
            : Result<UpdatesReport>.Fail(answer.Error!);
    }

    /// <inheritdoc />
    public async Task<Result<DownloadedPackageSource>> DownloadPackageAsync(
        string moduleId,
        string version,
        CancellationToken ct)
    {
        var transport = Choose(await _switch.IsEnabledAsync(ct));

        var details = await GetModuleAsync(moduleId, NeutralLocale, ct);
        if (!details.IsSuccess)
        {
            return Result<DownloadedPackageSource>.Fail(details.Error!);
        }

        var entry = details.Value.Versions.FirstOrDefault(candidate =>
        {
            return string.Equals(candidate.Version, version, StringComparison.Ordinal);
        });
        if (entry is null)
        {
            return Result<DownloadedPackageSource>.Fail(MarketplaceClientRefusals.ModuleNotInCatalogue);
        }

        if (entry.Revoked || details.Value.Module.Revoked)
        {
            return Result<DownloadedPackageSource>.Fail(MarketplaceClientRefusals.PackageRevoked);
        }

        var grantPath = $"{ApiPrefix}/modules/{Uri.EscapeDataString(moduleId)}"
            + $"/versions/{Uri.EscapeDataString(version)}/download-grant";
        var answer = await transport.SendAsync(
            new MarketplaceRequest(
                HttpMethod.Post, grantPath, NeutralLocale, LeaseRequirement.Required, ResponseLimit.Grant),
            ct);
        if (!answer.IsSuccess)
        {
            return Result<DownloadedPackageSource>.Fail(answer.Error!);
        }

        var grant = await _reader.ReadVerifiedAsync<DownloadGrant>(answer.Value, ct);
        if (!grant.IsSuccess)
        {
            return Result<DownloadedPackageSource>.Fail(grant.Error!);
        }

        var expected = new ExpectedModulePackage(
            moduleId, entry.Version, entry.Sha256.ToLowerInvariant(), entry.SizeBytes);
        return await _downloader.DownloadAsync(transport, grant.Value, expected, ct);
    }

    /// <inheritdoc />
    public Task<Result<MarketplaceKeyStatus>> GetKeyStatusAsync(CancellationToken ct)
    {
        return _checker.GetStatusAsync(ct);
    }

    /// <inheritdoc />
    public async Task<Result<MarketplaceKeyStatus>> RefreshKeysAsync(CancellationToken ct)
    {
        var transport = Choose(await _switch.IsEnabledAsync(ct));

        var answer = await transport.SendAsync(
            new MarketplaceRequest(
                HttpMethod.Get, $"{ApiPrefix}/keys", NeutralLocale, LeaseRequirement.None, ResponseLimit.Catalogue),
            ct);
        if (!answer.IsSuccess)
        {
            return Result<MarketplaceKeyStatus>.Fail(answer.Error!);
        }

        var keys = await _reader.ReadDocumentAsync<MarketplaceKeySet>(answer.Value, ct);
        if (!keys.IsSuccess)
        {
            return Result<MarketplaceKeyStatus>.Fail(keys.Error!);
        }

        var accepted = _checker.AcceptRotation(keys.Value);
        return accepted.IsSuccess
            ? await _checker.GetStatusAsync(ct)
            : Result<MarketplaceKeyStatus>.Fail(accepted.Error!);
    }

    /// <summary>Picks the transport the stored decision allows.</summary>
    /// <param name="enabled">Whether the marketplace is switched on.</param>
    /// <returns>The real transport when on; the one that goes nowhere when off.</returns>
    private IMarketplaceTransport Choose(bool enabled)
    {
        return enabled ? _http : _disabled;
    }

    /// <summary>Answers a conditional request the host said had not changed.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cached">The entry that was revalidated.</param>
    /// <returns>The held page with a renewed time-to-live, unless its signature has grown too old.</returns>
    private Result<CataloguePage> Revalidated(CatalogueCacheKey key, CatalogueCacheEntry cached)
    {
        var fresh = _checker.CheckFreshness(cached.Page);
        if (!fresh.IsSuccess)
        {
            _cache.Remove(key);
            return Result<CataloguePage>.Fail(fresh.Error!);
        }

        _cache.Store(key, cached.Page, cached.ETag);
        return Result<CataloguePage>.Ok(cached.Page);
    }
}
