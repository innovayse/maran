using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// A marketplace client that answers from what a test sets, counts what it was asked, and hands out real
/// downloaded-package files so the cleanup that deletes them can be observed.
/// </summary>
public sealed class FakeMarketplaceClient : IMarketplaceClient
{
    /// <summary>The folder the downloaded files are written to.</summary>
    private readonly string _directory;

    /// <summary>The catalogue page, or the failure, <see cref="ListCatalogueAsync"/> answers with.</summary>
    public Result<CataloguePage> Page { get; set; } = Result<CataloguePage>.Fail(MarketplaceClientRefusals.Disabled);

    /// <summary>The modules <see cref="GetModuleAsync"/> knows, by id.</summary>
    public Dictionary<string, CatalogueModuleDetails> Details { get; } = new(StringComparer.Ordinal);

    /// <summary>The report, or the failure, <see cref="CheckUpdatesAsync"/> answers with.</summary>
    public Result<UpdatesReport> Updates { get; set; } = Result<UpdatesReport>.Fail(MarketplaceClientRefusals.Disabled);

    /// <summary>When set, <see cref="GetModuleAsync"/> and <see cref="DownloadPackageAsync"/> fail with it.</summary>
    public Error? Failure { get; set; }

    /// <summary>When set, <see cref="DownloadPackageAsync"/> alone fails with it.</summary>
    public Error? DownloadFailure { get; set; }

    /// <summary>How many catalogue, details and update calls were made.</summary>
    public int ReadCalls { get; private set; }

    /// <summary>How many downloads were requested.</summary>
    public int DownloadCalls { get; private set; }

    /// <summary>The paths of the files handed out, so a test can see they were deleted.</summary>
    public List<string> DownloadedFiles { get; } = [];

    /// <summary>The installed references the last update check sent.</summary>
    public IReadOnlyList<InstalledModuleReference> LastReferences { get; private set; } = [];

    /// <summary>Creates the fake.</summary>
    /// <param name="directory">A folder the downloaded files are written to.</param>
    public FakeMarketplaceClient(string directory)
    {
        _directory = directory;
    }

    /// <inheritdoc />
    public Task<Result<CataloguePage>> ListCatalogueAsync(
        string channel,
        string locale,
        string? search,
        string? cursor,
        CancellationToken ct)
    {
        ReadCalls++;
        return Task.FromResult(Page);
    }

    /// <inheritdoc />
    public Task<Result<CatalogueModuleDetails>> GetModuleAsync(string moduleId, string locale, CancellationToken ct)
    {
        ReadCalls++;

        if (Failure is not null)
        {
            return Task.FromResult(Result<CatalogueModuleDetails>.Fail(Failure));
        }

        return Task.FromResult(Details.TryGetValue(moduleId, out var details)
            ? Result<CatalogueModuleDetails>.Ok(details)
            : Result<CatalogueModuleDetails>.Fail(MarketplaceClientRefusals.ModuleNotInCatalogue));
    }

    /// <inheritdoc />
    public Task<Result<UpdatesReport>> CheckUpdatesAsync(
        IReadOnlyList<InstalledModuleReference> installed,
        string channel,
        string locale,
        CancellationToken ct)
    {
        ReadCalls++;
        LastReferences = installed;
        return Task.FromResult(Updates);
    }

    /// <inheritdoc />
    public Task<Result<DownloadedPackageSource>> DownloadPackageAsync(
        string moduleId,
        string version,
        CancellationToken ct)
    {
        DownloadCalls++;

        var failure = DownloadFailure ?? Failure;
        if (failure is not null)
        {
            return Task.FromResult(Result<DownloadedPackageSource>.Fail(failure));
        }

        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".ipm");
        File.WriteAllBytes(path, [1, 2, 3]);
        DownloadedFiles.Add(path);

        var package = new DownloadedPackageSource(
            path,
            new ExpectedModulePackage(moduleId, version, CatalogueSamples.Digest, SizeBytes: 3),
            "marketplace test");
        return Task.FromResult(Result<DownloadedPackageSource>.Ok(package));
    }

    /// <inheritdoc />
    public Task<Result<MarketplaceKeyStatus>> GetKeyStatusAsync(CancellationToken ct)
    {
        return Task.FromResult(Result<MarketplaceKeyStatus>.Ok(new MarketplaceKeyStatus(1, 0)));
    }

    /// <inheritdoc />
    public Task<Result<MarketplaceKeyStatus>> RefreshKeysAsync(CancellationToken ct)
    {
        return Task.FromResult(Result<MarketplaceKeyStatus>.Ok(new MarketplaceKeyStatus(1, 0)));
    }
}
