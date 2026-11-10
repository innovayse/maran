using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Jobs;
using Maran.Modules.Marketplace.Models;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Carries out an accepted install, update, remove or rollback: the step sequence the task shows, the
/// call to the loader, the ledger entry, and the cleanup that must happen however the run ends.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written forward from the question: if this fails halfway, what is the panel left running?</b> The
/// answer is the previous version, always. Nothing is applied until the loader has verified and staged the
/// package, and a staged package takes effect only at the next panel start, so a download that fails, a
/// package whose signature or manifest is refused, or a loader that refuses (the open panel's loader
/// refuses everything) changes nothing that runs. The failure is recorded as the task's coded failure and
/// as a refused audit entry, and nothing else moves.
/// </para>
/// <para>
/// <b>The steps are operator English</b>, not localized: they are task lines. For an install and an update
/// the entitlement check is the grant request the download begins with (the service decides and the panel
/// relays), so "Checking entitlement" and "Downloading" are one exchange reported as the two things the
/// administrator is waiting for. The loader verifies the package's signature and stages it in one call, so
/// "Verifying signature" is reported before it and "Staging" after it succeeds, when the ledger entry is
/// written. The last line says the work is staged and needs a restart, because migrations run at the next
/// start and the module's routes appear after it.
/// </para>
/// <para>
/// <b>Cleanup is not optional.</b> In a <c>finally</c>: the operation gate is released (which also ends its
/// record of the open task) and the catalogue cache is dropped, so a failed run does not leave the module locked
/// for thirty minutes. A run stopped by shutdown or by an unexpected exception fails its task and is
/// audited before the exception continues.
/// </para>
/// </remarks>
public sealed class ModuleOperationRunner
{
    /// <summary>The first line of an install or an update.</summary>
    public const string CheckingEntitlementLine = "Checking entitlement";

    /// <summary>The line while the package is fetched.</summary>
    public const string DownloadingLine = "Downloading";

    /// <summary>The line while the loader verifies the package's signature.</summary>
    public const string VerifyingSignatureLine = "Verifying signature";

    /// <summary>The line while the change is staged and recorded.</summary>
    public const string StagingLine = "Staging";

    /// <summary>The last line of a run that succeeded.</summary>
    public const string DoneLine = "Done - restart required";

    /// <summary>Progress reported at the start of an install or an update.</summary>
    private const int CheckingPercent = 5;

    /// <summary>Progress reported while downloading.</summary>
    private const int DownloadingPercent = 20;

    /// <summary>Progress reported while the loader verifies.</summary>
    private const int VerifyingPercent = 60;

    /// <summary>Progress reported while staging.</summary>
    private const int StagingPercent = 85;

    /// <summary>Progress reported when the run is done.</summary>
    private const int DonePercent = 100;

    /// <summary>Logged when a run ends with an exception; carries the exception's type and never its text.</summary>
    private static readonly Action<ILogger, string, string, string, Exception?> LogUnexpected =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            new EventId(1, nameof(ModuleOperationRunner)),
            "Module operation {Operation} on {ModuleId} stopped with an unexpected {ExceptionType}; the task was failed and the previous version keeps running");

    /// <summary>The marketplace client, for the download.</summary>
    private readonly IMarketplaceClient _client;

    /// <summary>The loader that verifies and stages a package.</summary>
    private readonly IModuleLoader _loader;

    /// <summary>The ledger of what the panel staged.</summary>
    private readonly InstalledPackageLedger _ledger;

    /// <summary>The per-module operation gate.</summary>
    private readonly ModuleOperationGate _gate;

    /// <summary>The audit journal.</summary>
    private readonly MarketplaceAuditJournal _journal;

    /// <summary>The task recorder.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The catalogue cache, dropped when a run ends.</summary>
    private readonly CatalogueCache _cache;

    /// <summary>The logger.</summary>
    private readonly ILogger<ModuleOperationRunner> _logger;

    /// <summary>Creates the runner.</summary>
    /// <param name="client">The marketplace client, for the download.</param>
    /// <param name="loader">The loader that verifies and stages a package.</param>
    /// <param name="ledger">The ledger of what the panel staged.</param>
    /// <param name="gate">The per-module operation gate.</param>
    /// <param name="journal">The audit journal.</param>
    /// <param name="tasks">The task recorder.</param>
    /// <param name="cache">The catalogue cache, dropped when a run ends.</param>
    /// <param name="logger">The logger.</param>
    public ModuleOperationRunner(
        IMarketplaceClient client,
        IModuleLoader loader,
        InstalledPackageLedger ledger,
        ModuleOperationGate gate,
        MarketplaceAuditJournal journal,
        ITaskRecorder tasks,
        CatalogueCache cache,
        ILogger<ModuleOperationRunner> logger)
    {
        _client = client;
        _loader = loader;
        _ledger = ledger;
        _gate = gate;
        _journal = journal;
        _tasks = tasks;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Runs one accepted operation to its end.</summary>
    /// <param name="request">The accepted operation.</param>
    /// <param name="ct">Cancellation token; the host's shutdown token when the bus runs it.</param>
    /// <returns>A task that completes when the operation has ended and its task is completed or failed.</returns>
    public async Task RunAsync(ModuleOperationRequested request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var outcome = await ExecuteAsync(request, ct);

            if (outcome.IsSuccess)
            {
                await _tasks.ReportAsync(request.TaskId, DonePercent, DoneLine, ct);
                await _tasks.CompleteAsync(request.TaskId, ct);
            }
            else
            {
                await FailAsync(request, outcome.Error!);
            }
        }
        catch (Exception exception)
        {
            // Every way the run can end without an outcome, not a list of them: the task must not stay
            // running and the attempt must be on the audit trail. Rethrown unchanged, so nothing is
            // swallowed and the bus logs it as it logs any failed message.
            LogUnexpected(_logger, request.Operation.ToString(), request.ModuleId, exception.GetType().Name, null);
            await FailAsync(request, MarketplaceOperationRefusals.OperationFailed);
            throw;
        }
        finally
        {
            await _gate.ReleaseAsync(request.Claim, CancellationToken.None);
            _cache.Invalidate();
        }
    }

    /// <summary>Does the work of one operation and reports how it ended.</summary>
    /// <param name="request">The accepted operation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the coded failure.</returns>
    private async Task<Result> ExecuteAsync(ModuleOperationRequested request, CancellationToken ct)
    {
        return request.Operation switch
        {
            MarketplaceOperation.Install => await StageFromMarketplaceAsync(request, upgrade: false, ct),
            MarketplaceOperation.Update => await StageFromMarketplaceAsync(request, upgrade: true, ct),
            MarketplaceOperation.Remove => await RemoveAsync(request, ct),
            MarketplaceOperation.Rollback => await RollbackAsync(request, ct),
            _ => throw new InvalidOperationException("MarketplaceOperation has four members."),
        };
    }

    /// <summary>Downloads a package, hands it to the loader and records what was staged.</summary>
    /// <param name="request">The accepted install or update.</param>
    /// <param name="upgrade">True for an update, false for an install.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the coded failure.</returns>
    private async Task<Result> StageFromMarketplaceAsync(
        ModuleOperationRequested request,
        bool upgrade,
        CancellationToken ct)
    {
        await _tasks.ReportAsync(request.TaskId, CheckingPercent, CheckingEntitlementLine, ct);
        await _tasks.ReportAsync(request.TaskId, DownloadingPercent, DownloadingLine, ct);

        var download = await _client.DownloadPackageAsync(request.ModuleId, request.Version!, ct);
        if (!download.IsSuccess)
        {
            return Result.Fail(download.Error!);
        }

        // Disposed at the end of this method, which deletes the downloaded file: the loader keeps its own
        // verified staged copy, and a failed run leaves nothing behind.
        await using var package = download.Value;

        await _tasks.ReportAsync(request.TaskId, VerifyingPercent, VerifyingSignatureLine, ct);
        var staged = upgrade
            ? await _loader.UpgradeAsync(package, ct)
            : await _loader.InstallAsync(package, ct);
        if (!staged.IsSuccess)
        {
            return Result.Fail(staged.Error!);
        }

        await _tasks.ReportAsync(request.TaskId, StagingPercent, StagingLine, ct);
        await _ledger.RecordStagedAsync(
            request.ModuleId, request.Version!, package.Expected.Sha256, request.UserId, ct, request.DisplayName);

        return Result.Ok();
    }

    /// <summary>Unloads a module and closes its ledger history, keeping its data.</summary>
    /// <param name="request">The accepted remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the coded failure.</returns>
    private async Task<Result> RemoveAsync(ModuleOperationRequested request, CancellationToken ct)
    {
        await _tasks.ReportAsync(request.TaskId, StagingPercent, StagingLine, ct);

        var unloaded = await _loader.UnloadAsync(request.ModuleId, ct);
        if (!unloaded.IsSuccess)
        {
            return unloaded;
        }

        await _ledger.RecordRemovalAsync(request.ModuleId, ct);
        return Result.Ok();
    }

    /// <summary>Restores the previous version through the loader and records it as staged.</summary>
    /// <param name="request">The accepted rollback.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the coded failure.</returns>
    private async Task<Result> RollbackAsync(ModuleOperationRequested request, CancellationToken ct)
    {
        await _tasks.ReportAsync(request.TaskId, StagingPercent, StagingLine, ct);

        var current = await _ledger.FindCurrentAsync(request.ModuleId, ct);
        if (current is null)
        {
            return Result.Fail(MarketplaceOperationRefusals.ModuleNotInstalled);
        }

        var history = await _ledger.ListHistoryAsync(request.ModuleId, ct);
        var previous = history.FirstOrDefault(row =>
        {
            return current.PreviousVersion is not null
                && string.Equals(row.Version, current.PreviousVersion, StringComparison.Ordinal);
        });
        if (previous is null)
        {
            return Result.Fail(MarketplaceOperationRefusals.NoPreviousVersion);
        }

        var restored = await _loader.RollbackAsync(request.ModuleId, ct);
        if (!restored.IsSuccess)
        {
            return restored;
        }

        await _ledger.RecordStagedAsync(
            request.ModuleId, previous.Version, previous.PackageSha256, request.UserId, ct, previous.DisplayName);
        return Result.Ok();
    }

    /// <summary>Fails the task with the error's code and writes the refused audit entry.</summary>
    /// <param name="request">The operation that failed.</param>
    /// <param name="error">Why it failed.</param>
    /// <returns>A task that completes when both are written.</returns>
    private async Task FailAsync(ModuleOperationRequested request, Error error)
    {
        await _tasks.FailAsync(request.TaskId, error.Code, CancellationToken.None);
        await _journal.RecordRequestAsync(
            request.Operation,
            new ModulePackageReference(request.ModuleId, request.Version, PackageSha256: null),
            error,
            request.IpAddress,
            request.UserAgent,
            CancellationToken.None);
    }
}
