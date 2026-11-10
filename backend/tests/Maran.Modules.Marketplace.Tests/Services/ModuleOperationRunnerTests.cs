using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Jobs;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Services;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>
/// The runner ends every run the same way however it went: the task is completed or failed, the failure is
/// audited, and the gate (with its record of the open task) and the catalogue cache are cleaned up.
/// </summary>
public sealed class ModuleOperationRunnerTests
{
    /// <summary>Starts an install of <c>mail</c> 1.2.0 and returns the accepted request without running it.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The request the dispatcher was handed.</returns>
    private static async Task<ModuleOperationRequested> AcceptedInstallAsync(OperationWorld world)
    {
        world.Offer(CatalogueSamples.Module("mail", CatalogueSamples.Version("1.2.0")), CatalogueSamples.Version("1.2.0"));
        await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        return Assert.Single(world.Dispatcher.Dispatched);
    }

    /// <summary>A successful run drops the catalogue cache and releases the gate, which ends the record of the open task.</summary>
    [Fact]
    public async Task A_finished_run_cleans_up_the_gate_the_open_record_and_the_cache()
    {
        using var world = new OperationWorld();
        var request = await AcceptedInstallAsync(world);
        var key = CatalogueCache.KeyOf("stable", "en", null, null);
        world.Cache.Store(key, CatalogueSamples.Page(), etag: null);

        await world.Runner.RunAsync(request, CancellationToken.None);

        Assert.Null(world.Cache.Find(key));
        Assert.Null(await world.Gate.FindOpenAsync("mail", MarketplaceOperation.Install, "1.2.0", CancellationToken.None));
        Assert.True((await world.Gate.AcquireAsync("mail", CancellationToken.None)).IsSuccess);
    }

    /// <summary>A failed run cleans up exactly the same, so a failure does not lock the module.</summary>
    [Fact]
    public async Task A_failed_run_cleans_up_too()
    {
        using var world = new OperationWorld();
        var request = await AcceptedInstallAsync(world);
        world.Loader.Refusal = Error.Of("MarketplaceNotAvailable", ErrorType.Unavailable);
        var key = CatalogueCache.KeyOf("stable", "en", null, null);
        world.Cache.Store(key, CatalogueSamples.Page(), etag: null);

        await world.Runner.RunAsync(request, CancellationToken.None);

        Assert.Single(world.Tasks.Failed);
        Assert.Null(world.Cache.Find(key));
        Assert.Null(await world.Gate.FindOpenAsync("mail", MarketplaceOperation.Install, "1.2.0", CancellationToken.None));
        Assert.True((await world.Gate.AcquireAsync("mail", CancellationToken.None)).IsSuccess);
        Assert.All(world.Client.DownloadedFiles, path => { Assert.False(File.Exists(path)); });
    }

    /// <summary>A run stopped by an exception fails its task, is audited as failed, frees the module and lets the exception continue.</summary>
    [Fact]
    public async Task A_run_stopped_by_an_exception_fails_the_task_and_lets_it_continue()
    {
        using var world = new OperationWorld();
        var request = await AcceptedInstallAsync(world);
        var runner = new ModuleOperationRunner(
            world.Client,
            new ThrowingLoader(),
            world.Ledger,
            world.Gate,
            world.Journal,
            world.Tasks,
            world.Cache,
            NullLogger<ModuleOperationRunner>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await runner.RunAsync(request, CancellationToken.None);
        });

        Assert.Equal(ErrorCodes.ModuleOperationFailed, Assert.Single(world.Tasks.Failed).Code);
        Assert.Contains(
            world.AuditFor(MarketplaceClientAuditActions.ModuleInstallRequested),
            entry => { return !entry.Succeeded && entry.Subject.Contains("reason=ModuleOperationFailed", StringComparison.Ordinal); });
        Assert.True((await world.Gate.AcquireAsync("mail", CancellationToken.None)).IsSuccess);
    }

    /// <summary>The audit entry for a failed run names the module and version and carries no address of the marketplace, grant or error text.</summary>
    [Fact]
    public async Task The_failure_entry_carries_the_module_and_version_and_nothing_secret()
    {
        using var world = new OperationWorld();
        var request = await AcceptedInstallAsync(world);
        world.Client.DownloadFailure = MarketplaceClientRefusals.DownloadDigestMismatch;

        await world.Runner.RunAsync(request, CancellationToken.None);

        var failed = Assert.Single(world.AuditFor(MarketplaceClientAuditActions.ModuleInstallRequested), entry => { return !entry.Succeeded; });
        Assert.Equal("module=mail;version=1.2.0;digest=-;reason=DownloadDigestMismatch", failed.Subject);
        Assert.Equal("203.0.113.5", failed.IpAddress);
    }

    /// <summary>A loader that throws on any call, standing for a fault the runner cannot name.</summary>
    private sealed class ThrowingLoader : IModuleLoader
    {
        /// <inheritdoc />
        public Task<Result<ModuleInstallResult>> InstallAsync(IModulePackageSource source, CancellationToken ct)
        {
            throw new InvalidOperationException("The loader broke.");
        }

        /// <inheritdoc />
        public Task<Result<ModuleInstallResult>> UpgradeAsync(IModulePackageSource source, CancellationToken ct)
        {
            throw new InvalidOperationException("The loader broke.");
        }

        /// <inheritdoc />
        public Task<Result> RollbackAsync(string moduleId, CancellationToken ct)
        {
            throw new InvalidOperationException("The loader broke.");
        }

        /// <inheritdoc />
        public Task<Result> UnloadAsync(string moduleId, CancellationToken ct)
        {
            throw new InvalidOperationException("The loader broke.");
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<LoadedModuleInfo>> ListAsync(CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<LoadedModuleInfo>>([]);
        }
    }
}
