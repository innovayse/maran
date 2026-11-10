using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Commands.PullAccountImport;
using Maran.Modules.ServerMigration.Commands.StartAccountImport;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Jobs;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Queries.GetAccountImport;
using Maran.Modules.ServerMigration.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Runs ONE source account of a migration job through its two phases: the initial sync (create the account, copy the
/// files, load the databases) with the source still serving, and the final delta at cut-over (issue #197, phase 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>It acts as the administrator who created the job, and as nobody else</b> (<see cref="IJobCallerScopes"/>,
/// rebuilt from the user table on every run), for the reasons <see cref="AccountImportOrchestrator"/> states. A message
/// carries ids only; the key and the grant are read from the rows at the moment they are used.
/// </para>
/// <para>
/// <b>Failure is per account and never blocks the others.</b> A failure before the first sync finished deletes the
/// account this job created, through the same rollback an import uses, which refuses an account older than the run. A
/// failure at the cut-over keeps the account: it was already synced and deleting it on a hiccup of the last pass would
/// throw away what the administrator waited for. A rollback that cannot finish ends as
/// <see cref="MigrationAccountState.RolledBackIncomplete"/> naming the account.
/// </para>
/// <para>
/// <b>Pause and cancel are the same mechanism with different endings.</b> The worker's token is cancelled; the stream
/// to the agent ends at the agent's next boundary (a pull keeps its partial state), and the registry says why. The
/// importers are deliberately NOT stoppable: they create an account in several modules, and a half-run set cannot be
/// told from a finished one, so a pause takes effect after them.
/// </para>
/// <para>
/// <b>A source that is a Maran panel</b> is moved by the #127 path (pull the package, import it, same rollback)
/// instead of rsync; its cut-over only verifies, because the administrator suspends the old account on its own panel
/// (decision D15).
/// </para>
/// </remarks>
public sealed class MigrationAccountTransfer
{
    /// <summary>The one log line for a run that faulted: the account row, the stage, the exception for the operator.</summary>
    private static readonly Action<ILogger, Guid, string, Exception?> LogFaulted =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Error,
            new EventId(1, nameof(MigrationAccountTransfer)),
            "Migration account {AccountId} faulted at stage {Stage}.");

    /// <summary>How often the state of a Maran import is read while it runs.</summary>
    private static readonly TimeSpan ImportPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Opens the scope the run acts as the initiating administrator in.</summary>
    private readonly IJobCallerScopes _callers;

    /// <summary>Every module's importer, in the order they run.</summary>
    private readonly IReadOnlyList<IAccountMigrationImporter> _importers;

    /// <summary>Deletes the account a failed run created.</summary>
    private readonly IAccountMigrationRollback? _rollback;

    /// <summary>The agent's connection to remote servers.</summary>
    private readonly IAgentTransferClient _agent;

    /// <summary>What this process runs and how to stop it.</summary>
    private readonly MigrationRunRegistry _registry;

    /// <summary>Fetches the migrated sites for the comparison report.</summary>
    private readonly MigrationVerifier _verifier;

    /// <summary>Dispatches the import commands of a Maran source and the scheduler's tick.</summary>
    private readonly IMessageBus _bus;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the outcome.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Where an unexpected fault is logged.</summary>
    private readonly ILogger<MigrationAccountTransfer> _logger;

    /// <summary>Creates the engine.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="callers">Opens the scope the run acts as the initiating administrator in.</param>
    /// <param name="importers">Every module's importer.</param>
    /// <param name="rollbacks">Deletes the account a failed run created; a collection, because an absent module registers nothing.</param>
    /// <param name="agent">The agent's connection to remote servers.</param>
    /// <param name="registry">What this process runs and how to stop it.</param>
    /// <param name="verifier">Fetches the migrated sites.</param>
    /// <param name="bus">Dispatches commands and the tick.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the outcome.</param>
    /// <param name="logger">Where an unexpected fault is logged.</param>
    public MigrationAccountTransfer(
        ServerMigrationDbContext dbContext,
        IJobCallerScopes callers,
        IEnumerable<IAccountMigrationImporter> importers,
        IEnumerable<IAccountMigrationRollback> rollbacks,
        IAgentTransferClient agent,
        MigrationRunRegistry registry,
        MigrationVerifier verifier,
        IMessageBus bus,
        IClock clock,
        ServerMigrationAuditJournal journal,
        ILogger<MigrationAccountTransfer> logger)
    {
        _dbContext = dbContext;
        _callers = callers;
        _importers = importers.OrderBy(importer =>
        {
            return importer.Order;
        }).ToList();
        _rollback = rollbacks.FirstOrDefault();
        _agent = agent;
        _registry = registry;
        _verifier = verifier;
        _bus = bus;
        _clock = clock;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Runs one pass of one account to its end.</summary>
    /// <param name="accountId">The job's row for the account; it must already be in the state the pass starts from.</param>
    /// <param name="kind">Which pass.</param>
    /// <param name="shutdown">The host's shutdown token.</param>
    /// <returns>Resolves when the pass ended, however it ended.</returns>
    public async Task RunAsync(Guid accountId, MigrationRunKind kind, CancellationToken shutdown)
    {
        var account = await _dbContext.MigrationJobAccounts.FirstOrDefaultAsync(row => row.Id == accountId, shutdown);
        if (account is null || !StartsFrom(account, kind))
        {
            return;
        }

        var job = await _dbContext.MigrationJobs.FirstAsync(row => row.Id == account.JobId, shutdown);
        var server = await _dbContext.RemoteServers.FirstOrDefaultAsync(row => row.Id == job.RemoteServerId, shutdown);
        var token = _registry.Register(account.Id, shutdown);

        try
        {
            using var scope = await _callers.BeginAsync(job.CreatedByUserId, CancellationToken.None);
            if (scope is null || server is null || server.HostKey is null)
            {
                await EndAsync(account, kind, nameof(ErrorMessages.MigrationInitiatorGone), CancellationToken.None);

                return;
            }

            await RunScopedAsync(account, job, server, kind, token);
        }
        finally
        {
            _registry.Release(account.Id);
            await _bus.PublishAsync(new MigrationJobTickRequested(job.Id));
        }
    }

    /// <summary>Whether the row is in the state the pass starts from.</summary>
    /// <param name="account">The row.</param>
    /// <param name="kind">The pass.</param>
    /// <returns><c>true</c> when the scheduler or the cut-over command had already moved it there.</returns>
    private static bool StartsFrom(MigrationJobAccount account, MigrationRunKind kind)
    {
        return kind == MigrationRunKind.Initial
            ? account.State == MigrationAccountState.Running
            : account.State == MigrationAccountState.CuttingOver;
    }

    /// <summary>Runs the pass inside the administrator's scope and records how it ended.</summary>
    /// <param name="account">The row.</param>
    /// <param name="job">The job.</param>
    /// <param name="server">The registered server, with its host key pinned.</param>
    /// <param name="kind">The pass.</param>
    /// <param name="token">The worker's token.</param>
    /// <returns>Resolves when the row records the outcome.</returns>
    private async Task RunScopedAsync(
        MigrationJobAccount account,
        MigrationJob job,
        RemoteServer server,
        MigrationRunKind kind,
        CancellationToken token)
    {
        try
        {
            var outcome = kind == MigrationRunKind.Initial
                ? await InitialAsync(account, job, server, token)
                : await CutOverAsync(account, job, server, token);

            if (outcome is not null)
            {
                await EndAsync(account, kind, outcome.Code, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            await StoppedAsync(account);
        }
        catch (Exception exception)
        {
            LogFaulted(_logger, account.Id, account.Stage ?? string.Empty, exception);
            await EndAsync(account, kind, nameof(ErrorMessages.MigrationTransferFaulted), CancellationToken.None);
        }
    }

    /// <summary>Creates the account, copies its files and loads its databases, with the source still serving.</summary>
    /// <returns><c>null</c> when the account is synced, otherwise the failure.</returns>
    private async Task<Error?> InitialAsync(
        MigrationJobAccount account,
        MigrationJob job,
        RemoteServer server,
        CancellationToken token)
    {
        if (job.SourceUrl is not null)
        {
            return await ImportFromMaranAsync(account, job, token);
        }

        if (account.TargetAccountId is not null && !account.Provisioned)
        {
            var deleted = await DeleteAsync(account);
            if (!deleted.IsSuccess)
            {
                return deleted.Error;
            }

            account.AccountDeleted();
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }

        if (!account.Provisioned)
        {
            var failure = await ProvisionAsync(account);
            if (failure is not null)
            {
                return failure;
            }
        }

        token.ThrowIfCancellationRequested();
        var transferred = await TransferAsync(account, job, server, AgentPullPhase.Initial, null, token);
        if (transferred is not null)
        {
            return transferred;
        }

        account.Synced(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.AccountSynced, account.SourceAccountName, string.Empty, string.Empty, CancellationToken.None);

        return null;
    }

    /// <summary>The final delta and the verification of a synced account.</summary>
    /// <returns><c>null</c> when the account is cut over, otherwise the failure.</returns>
    private async Task<Error?> CutOverAsync(
        MigrationJobAccount account,
        MigrationJob job,
        RemoteServer server,
        CancellationToken token)
    {
        var databases = new List<MigrationDatabaseReport>();
        if (job.SourceUrl is null)
        {
            var transferred = await TransferAsync(account, job, server, AgentPullPhase.Final, databases, token);
            if (transferred is not null)
            {
                return transferred;
            }
        }

        account.AtStage("verify");
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        var domains = MigrationSectionMapper.ReadInventory(account).Sites
            .Where(site =>
            {
                return site.Backend != DiscoveredSiteBackend.Proxy;
            })
            .Select(site =>
            {
                return site.Domain;
            })
            .ToList();
        var sites = await _verifier.ProbeAsync(domains, token);
        var report = new MigrationAccountReport(account.SourceBytes, account.SlicesCompleted, job.SourceUrl is null, databases, sites);

        account.Complete(JsonSerializer.Serialize(report, JsonSerializerOptions.Web), _clock.UtcNow);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.AccountCutOver, account.SourceAccountName, string.Empty, string.Empty, CancellationToken.None);

        return null;
    }

    /// <summary>Lets every importer create its part, in order, then records that the account is provisioned.</summary>
    /// <param name="account">The row.</param>
    /// <returns><c>null</c> when every importer succeeded, otherwise the first failure.</returns>
    private async Task<Error?> ProvisionAsync(MigrationJobAccount account)
    {
        var composition = MigrationSectionMapper.Compose(account);
        var target = new MigrationImportTarget(
            null,
            account.SourceAccountName,
            new MigrationImportChoices(account.PlanId!.Value, account.TargetResellerId));
        var secrets = new List<MigrationIssuedSecret>();

        foreach (var importer in _importers)
        {
            var section = composition.Sections.FirstOrDefault(row =>
            {
                return row.ModuleId == importer.ModuleId;
            });
            if (section is null)
            {
                continue;
            }

            account.AtStage(importer.ModuleId);
            await _dbContext.SaveChangesAsync(CancellationToken.None);

            // Not stoppable: see the remarks.
            var imported = await importer.ImportAsync(section, target, CancellationToken.None);
            if (!imported.IsSuccess)
            {
                return imported.Error;
            }

            secrets.AddRange(imported.Value!.Secrets);
            if (imported.Value.CreatedAccountId is { } createdId)
            {
                account.AccountCreated(createdId);
                await _dbContext.SaveChangesAsync(CancellationToken.None);
                target = target with { AccountId = createdId };
            }
        }

        if (target.AccountId is null)
        {
            return Error.Of(MigrationCodes.SectionInvalid, ErrorType.Validation);
        }

        account.Provision();
        account.HoldIssuedSecrets(
            JsonSerializer.Serialize(
                secrets.Select(secret =>
                {
                    return new { subject = secret.Subject, secret = secret.Secret.Reveal() };
                }),
                JsonSerializerOptions.Web));
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        return null;
    }

    /// <summary>Copies the files, then loads every database that kept its name.</summary>
    /// <param name="account">The account being moved.</param>
    /// <param name="job">The job it belongs to.</param>
    /// <param name="server">The remote server it is pulled from.</param>
    /// <param name="phase">Which pass of the pull this is.</param>
    /// <param name="loaded">Where the databases loaded are listed, or <c>null</c> when nobody reads the list.</param>
    /// <param name="token">Cancels the transfer.</param>
    /// <returns><c>null</c> on success, otherwise the failure.</returns>
    private async Task<Error?> TransferAsync(
        MigrationJobAccount account,
        MigrationJob job,
        RemoteServer server,
        AgentPullPhase phase,
        List<MigrationDatabaseReport>? loaded,
        CancellationToken token)
    {
        var endpoint = RemoteServerAgentMapper.EndpointOf(server);
        var credential = RemoteServerAgentMapper.CredentialOf(server);

        account.AtStage("files");
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        var pulled = await PullAsync(account, job, endpoint, credential, phase, token);
        if (pulled is not null)
        {
            return pulled;
        }

        account.AtStage("databases");
        account.DatabasePassStarted();
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        foreach (var database in MigrationSectionMapper.ReadInventory(account).Databases)
        {
            token.ThrowIfCancellationRequested();
            var suffix = ImportedDatabaseNamePolicy.SuffixOf(account.SourceAccountName, database.Name);
            if (suffix is null)
            {
                continue;
            }

            var target = account.SourceAccountName + "_" + suffix;
            var failure = await StreamDatabaseAsync(account, endpoint, credential, database.Name, target, loaded, token);
            if (failure is not null)
            {
                return failure;
            }
        }

        return null;
    }

    /// <summary>
    /// Pulls the files. The stream is restarted when the job's bandwidth limit changed, which is how a limit changed
    /// while the job runs takes effect: the agent resumes from the partial state.
    /// </summary>
    /// <returns><c>null</c> on success, otherwise the failure.</returns>
    private async Task<Error?> PullAsync(
        MigrationJobAccount account,
        MigrationJob job,
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        AgentPullPhase phase,
        CancellationToken token)
    {
        while (true)
        {
            var limit = await CurrentLimitAsync(job.Id, token);
            var request = new AgentFilePullRequest(
                endpoint, credential, account.SourceAccountName, account.SourceHome, phase, limit ?? 0, account.SourceDiskBytes ?? 0);
            using var restart = CancellationTokenSource.CreateLinkedTokenSource(token);
            var restarted = false;

            try
            {
                await foreach (var pullEvent in _agent.PullAccountFilesAsync(request, restart.Token).WithCancellation(restart.Token))
                {
                    switch (pullEvent)
                    {
                        case AgentFilePullProgress progress:
                            account.PullProgressed(progress.SlicesCompleted);
                            await _dbContext.SaveChangesAsync(CancellationToken.None);
                            if (await CurrentLimitAsync(job.Id, token) != limit)
                            {
                                restarted = true;
                                await restart.CancelAsync();
                            }

                            break;

                        case AgentFilePullDone done:
                            account.FilesPulled(done.Slices, done.SourceBytes);
                            await _dbContext.SaveChangesAsync(CancellationToken.None);

                            return null;

                        case AgentFilePullFailed failed:
                            return MigrationTransferErrorTranslator.Translate(failed.Error, failed.Reason, failed.TransferReason);
                    }
                }
            }
            catch (OperationCanceledException) when (restarted && !token.IsCancellationRequested)
            {
                continue;
            }

            return Error.Of(nameof(ErrorMessages.MigrationTransferFaulted), ErrorType.Failure);
        }
    }

    /// <summary>Dumps one database on the source and loads it through the restricted loader.</summary>
    /// <returns><c>null</c> on success, otherwise the failure.</returns>
    private async Task<Error?> StreamDatabaseAsync(
        MigrationJobAccount account,
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        string sourceName,
        string targetName,
        List<MigrationDatabaseReport>? loaded,
        CancellationToken token)
    {
        var request = new AgentDatabaseTransferRequest(
            endpoint, credential, sourceName, account.SourceAccountName, targetName, 0);

        await foreach (var transferEvent in _agent.StreamRemoteDatabaseAsync(request, token).WithCancellation(token))
        {
            switch (transferEvent)
            {
                case AgentDatabaseTransferDone done:
                    account.DatabaseLoaded(done.DumpBytes);
                    loaded?.Add(new MigrationDatabaseReport(targetName, done.DumpBytes));
                    await _dbContext.SaveChangesAsync(CancellationToken.None);

                    return null;

                case AgentDatabaseTransferFailed failed:
                    return MigrationTransferErrorTranslator.Translate(failed.Error, failed.Reason, failed.TransferReason);
            }
        }

        return Error.Of(nameof(ErrorMessages.MigrationTransferFaulted), ErrorType.Failure);
    }

    /// <summary>Moves a Maran source's account by the #127 path: pull the package, import it, wait.</summary>
    /// <returns><c>null</c> when the import completed, otherwise the failure.</returns>
    private async Task<Error?> ImportFromMaranAsync(MigrationJobAccount account, MigrationJob job, CancellationToken token)
    {
        if (string.IsNullOrEmpty(account.PullGrant) || job.ExpectedKeyFingerprint is null)
        {
            return Error.Of(nameof(ErrorMessages.MigrationGrantMissing), ErrorType.Conflict);
        }

        account.AtStage("pull");
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        var staged = await _bus.InvokeAsync<Result<AccountImportDto>>(
            new PullAccountImportCommand(job.SourceUrl!, new SensitiveString(account.PullGrant), job.ExpectedKeyFingerprint),
            token);
        if (!staged.IsSuccess)
        {
            return staged.Error;
        }

        account.AtStage("import");
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        var started = await _bus.InvokeAsync<Result<AccountImportDto>>(
            new StartAccountImportCommand(
                account.PlanId!.Value, account.TargetResellerId, account.SourceAccountName, job.ExpectedKeyFingerprint, staged.Value!.Id),
            token);
        if (!started.IsSuccess)
        {
            return started.Error;
        }

        while (true)
        {
            await Task.Delay(ImportPollInterval, token);
            var read = await _bus.InvokeAsync<Result<AccountImportDto>>(new GetAccountImportQuery(staged.Value.Id), CancellationToken.None);
            if (!read.IsSuccess)
            {
                return read.Error;
            }

            switch (read.Value!.State)
            {
                case nameof(AccountImportState.Completed):
                    if (read.Value.TargetAccountId is { } created)
                    {
                        account.AccountCreated(created);
                    }

                    account.Provision();
                    account.Synced(_clock.UtcNow);
                    await _dbContext.SaveChangesAsync(CancellationToken.None);

                    return null;

                case nameof(AccountImportState.RolledBack):
                case nameof(AccountImportState.RolledBackIncomplete):
                    // The import rolled itself back: nothing of it is left for this engine to delete.
                    account.AccountDeleted();

                    return Error.Of(read.Value.FailureCode ?? nameof(ErrorMessages.MigrationImportFaulted), ErrorType.Failure);
            }
        }
    }

    /// <summary>Reads the job's bandwidth limit now.</summary>
    /// <param name="jobId">The job.</param>
    /// <param name="token">Cancels the read.</param>
    /// <returns>The limit, or <c>null</c> for none.</returns>
    private async Task<int?> CurrentLimitAsync(Guid jobId, CancellationToken token)
    {
        return await _dbContext.MigrationJobs.AsNoTracking()
            .Where(row => row.Id == jobId)
            .Select(row => row.BandwidthLimitKib)
            .FirstOrDefaultAsync(token);
    }

    /// <summary>Deletes the account the run created, if the rollback is available.</summary>
    /// <param name="account">The row.</param>
    /// <returns>Success, or why the account remains.</returns>
    private async Task<Result> DeleteAsync(MigrationJobAccount account)
    {
        if (_rollback is null)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationRollbackUnavailable), ErrorType.Conflict));
        }

        return await _rollback.DeleteImportedAccountAsync(
            account.TargetAccountId!.Value, account.StartedAt!.Value, CancellationToken.None);
    }

    /// <summary>Records how a pass that did not succeed ended: a rollback before the first sync, a kept account after it.</summary>
    /// <param name="account">The row.</param>
    /// <param name="kind">The pass.</param>
    /// <param name="code">The failure code.</param>
    /// <param name="token">Cancels the writes.</param>
    /// <returns>Resolves when the row records the outcome.</returns>
    private async Task EndAsync(MigrationJobAccount account, MigrationRunKind kind, string code, CancellationToken token)
    {
        if (kind == MigrationRunKind.CutOver)
        {
            account.FailedAfterSync(code, _clock.UtcNow);
            await _dbContext.SaveChangesAsync(token);
            await _journal.RecordFailureAsync(
                MigrationAuditActions.AccountCutOverFailed, account.SourceAccountName, string.Empty, string.Empty, token);

            return;
        }

        await RollBackAsync(account, code, cancelled: false);
    }

    /// <summary>A pause or a cancel stopped the worker.</summary>
    /// <param name="account">The row.</param>
    /// <returns>Resolves when the row records the outcome.</returns>
    private async Task StoppedAsync(MigrationJobAccount account)
    {
        if (_registry.ReasonOf(account.Id) == MigrationStopReason.Cancel)
        {
            await RollBackAsync(account, nameof(ErrorMessages.MigrationAccountCancelled), cancelled: true);

            return;
        }

        // A pause, or the host shutting down: the partial state is kept and a resume continues.
        account.Paused();
        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Deletes the account the run created and records the outcome.</summary>
    /// <param name="account">The row.</param>
    /// <param name="code">The failure code.</param>
    /// <param name="cancelled">Whether the end is a cancellation (<c>true</c>) rather than a failure.</param>
    /// <returns>Resolves when the row records the outcome.</returns>
    private async Task RollBackAsync(MigrationJobAccount account, string code, bool cancelled)
    {
        var now = _clock.UtcNow;
        if (account.TargetAccountId is not null)
        {
            var deleted = await DeleteAsync(account);
            if (!deleted.IsSuccess)
            {
                account.RollbackFailed(code, now);
                await _dbContext.SaveChangesAsync(CancellationToken.None);
                await _journal.RecordFailureAsync(
                    MigrationAuditActions.AccountRollbackIncomplete, account.SourceAccountName, string.Empty, string.Empty, CancellationToken.None);

                return;
            }
        }

        if (cancelled)
        {
            account.Cancelled(now);
        }
        else
        {
            account.RollBack(code, now);
        }

        await _dbContext.SaveChangesAsync(CancellationToken.None);
        await _journal.RecordFailureAsync(
            cancelled ? MigrationAuditActions.AccountCancelled : MigrationAuditActions.AccountRolledBack,
            account.SourceAccountName,
            string.Empty,
            string.Empty,
            CancellationToken.None);
    }
}
