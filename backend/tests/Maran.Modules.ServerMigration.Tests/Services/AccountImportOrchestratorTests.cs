using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Services;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.ServerMigration.Tests.Services;

/// <summary>
/// What the import job does when it succeeds, and, as importantly, what it does and does not delete when it fails
/// (issue #127).
/// </summary>
public sealed class AccountImportOrchestratorTests
{
    /// <summary>The account the Accounts importer reports having created.</summary>
    private static readonly Guid CreatedAccount = new("0a1b2c3d-0000-4000-8000-000000000001");

    /// <summary>The sections of the package: accounts and databases.</summary>
    private const string Sections =
        "[{\"moduleId\":\"accounts\",\"version\":1,\"json\":\"{}\",\"resourceCount\":0},"
        + "{\"moduleId\":\"databases\",\"version\":1,\"json\":\"{}\",\"resourceCount\":1}]";

    /// <summary>The panel's clock.</summary>
    private readonly HandlerClock _clock = new();

    /// <summary>Opens the job-caller scope.</summary>
    private readonly ScriptedJobCallerScopes _callers = new();

    /// <summary>The Accounts importer, which creates the account.</summary>
    private readonly ScriptedAccountImporter _accounts = new("accounts", 0, CreatedAccount);

    /// <summary>The databases importer, which creates a login whose password is issued.</summary>
    private readonly ScriptedAccountImporter _databases = new("databases", 10);

    /// <summary>The Backups window.</summary>
    private readonly ScriptedArtifactService _artifacts = new();

    /// <summary>The account rollback.</summary>
    private readonly ScriptedAccountRollback _rollback = new();

    /// <summary>Records the task the job opens, reports on and closes.</summary>
    private readonly RecordedImportTasks _tasks = new();

    /// <summary>Records the audit entries.</summary>
    private readonly RecordedAuditEntries _audit = new();

    /// <summary>Builds an orchestrator and a running import over a private database.</summary>
    /// <param name="database">A name for the in-memory database.</param>
    /// <returns>The orchestrator, the database and the import's id.</returns>
    private (AccountImportOrchestrator Orchestrator, ServerMigrationDbContext Database, Guid ImportId) Build(string database)
    {
        var context = InMemoryMigrationDatabase.Open(database);
        var import = new AccountImport(
            Guid.NewGuid(),
            "alice",
            "alice.example",
            "source.example",
            new string('b', 64),
            Sections,
            1024,
            new MigrationImportedArtifact(Guid.NewGuid(), 2048, new string('c', 64), 1),
            AdministratorUser.UserId,
            _clock.UtcNow);
        import.Start(Guid.NewGuid(), null, _clock.UtcNow);
        context.AccountImports.Add(import);
        context.SaveChanges();

        var orchestrator = new AccountImportOrchestrator(
            context,
            _callers,
            [_databases, _accounts],
            [_artifacts],
            [_rollback],
            _tasks,
            _clock,
            new ServerMigrationAuditJournal(_audit, new AdministratorUser()),
            NullLogger<AccountImportOrchestrator>.Instance);

        return (orchestrator, context, import.Id);
    }

    /// <summary>Importers run in ascending order, the later ones are given the account the first created, and the job completes.</summary>
    [Fact]
    public async Task A_successful_import_runs_the_importers_in_order_and_completes()
    {
        var (orchestrator, database, importId) = Build(nameof(A_successful_import_runs_the_importers_in_order_and_completes));

        await orchestrator.RunAsync(importId, CancellationToken.None);

        var import = database.AccountImports.Single();
        Assert.Equal(AccountImportState.Completed, import.State);
        Assert.Equal(CreatedAccount, import.TargetAccountId);
        Assert.Equal(CreatedAccount, _databases.Targets.Single().AccountId);
        Assert.Null(_accounts.Targets.Single().AccountId);
        Assert.Equal(1, _artifacts.Restores);
        Assert.Empty(_rollback.Deleted);
        Assert.Equal(1, _callers.Opened);
        Assert.Equal(1, _callers.Closed);
    }

    /// <summary>A failing later importer deletes exactly the account the job created and ends the import as rolled back.</summary>
    [Fact]
    public async Task A_failed_importer_deletes_the_account_the_job_created_and_nothing_else()
    {
        var (orchestrator, database, importId) = Build(nameof(A_failed_importer_deletes_the_account_the_job_created_and_nothing_else));
        _databases.FailWith = Error.Of("DatabaseNameTaken", ErrorType.Conflict);

        await orchestrator.RunAsync(importId, CancellationToken.None);

        var import = database.AccountImports.Single();
        Assert.Equal(AccountImportState.RolledBack, import.State);
        Assert.Equal("DatabaseNameTaken", import.FailureCode);
        Assert.Equal([CreatedAccount], _rollback.Deleted);
        Assert.Equal(0, _artifacts.Restores);
    }

    /// <summary>A failed restore rolls the account back too, after every importer has run.</summary>
    [Fact]
    public async Task A_failed_restore_rolls_the_account_back()
    {
        var (orchestrator, database, importId) = Build(nameof(A_failed_restore_rolls_the_account_back));
        _artifacts.RestoreFailsWith = Error.Of("RestoreTruncated", ErrorType.Failure);

        await orchestrator.RunAsync(importId, CancellationToken.None);

        Assert.Equal(AccountImportState.RolledBack, database.AccountImports.Single().State);
        Assert.Equal([CreatedAccount], _rollback.Deleted);
    }

    /// <summary>When the account was never created there is nothing to delete, and the rollback is never asked to.</summary>
    [Fact]
    public async Task A_failure_before_the_account_exists_deletes_nothing()
    {
        var (orchestrator, database, importId) = Build(nameof(A_failure_before_the_account_exists_deletes_nothing));
        _accounts.FailWith = Error.Of("AccountNameTaken", ErrorType.Conflict);

        await orchestrator.RunAsync(importId, CancellationToken.None);

        var import = database.AccountImports.Single();
        Assert.Equal(AccountImportState.RolledBack, import.State);
        Assert.Null(import.TargetAccountId);
        Assert.Empty(_rollback.Deleted);
        Assert.Empty(_databases.Targets);
    }

    /// <summary>A rollback that cannot delete the account leaves the import saying so, with the account named.</summary>
    [Fact]
    public async Task A_rollback_that_fails_ends_the_import_as_incomplete_and_names_the_account()
    {
        var (orchestrator, database, importId) = Build(nameof(A_rollback_that_fails_ends_the_import_as_incomplete_and_names_the_account));
        _databases.FailWith = Error.Of("DatabaseNameTaken", ErrorType.Conflict);
        _rollback.FailWith = Error.Of("AccountDeletionFailed", ErrorType.Failure);

        await orchestrator.RunAsync(importId, CancellationToken.None);

        var import = database.AccountImports.Single();
        Assert.Equal(AccountImportState.RolledBackIncomplete, import.State);
        Assert.Equal(CreatedAccount, import.TargetAccountId);
        Assert.Contains(_audit.Entries, entry =>
        {
            return entry.Action == MigrationAuditActions.ImportRollbackIncomplete;
        });
    }

    /// <summary>
    /// A demoted or disabled initiator gets no scope, and the job creates nothing: without a scope the panel's
    /// tenant filters show the job nothing, and the job must halt rather than carry on blind.
    /// </summary>
    [Fact]
    public async Task An_initiator_who_can_no_longer_act_halts_the_job_before_anything_is_created()
    {
        var (orchestrator, database, importId) = Build(nameof(An_initiator_who_can_no_longer_act_halts_the_job_before_anything_is_created));
        _callers.InitiatorMayAct = false;

        await orchestrator.RunAsync(importId, CancellationToken.None);

        var import = database.AccountImports.Single();
        Assert.Equal(AccountImportState.RolledBack, import.State);
        Assert.Equal("MigrationInitiatorGone", import.FailureCode);
        Assert.Empty(_accounts.Targets);
        Assert.Empty(_rollback.Deleted);
    }

    /// <summary>An import that is not running is left alone, so a repeated message cannot run a job twice.</summary>
    [Fact]
    public async Task An_import_that_is_not_running_is_not_run()
    {
        var (orchestrator, database, importId) = Build(nameof(An_import_that_is_not_running_is_not_run));
        await orchestrator.RunAsync(importId, CancellationToken.None);
        var accountCalls = _accounts.Targets.Count;

        await orchestrator.RunAsync(importId, CancellationToken.None);

        Assert.Equal(accountCalls, _accounts.Targets.Count);
        Assert.Equal(AccountImportState.Completed, database.AccountImports.Single().State);
    }

    /// <summary>A successful import opens one administrators-only AccountImport task, reports each stage and completes it.</summary>
    [Fact]
    public async Task A_successful_import_is_one_task_that_reports_each_stage_and_completes()
    {
        var (orchestrator, _, importId) = Build(nameof(A_successful_import_is_one_task_that_reports_each_stage_and_completes));

        await orchestrator.RunAsync(importId, CancellationToken.None);

        var begun = Assert.Single(_tasks.Begun);
        Assert.Equal(TaskKinds.AccountImport, begun.Kind);
        Assert.Equal("alice", begun.Subject);
        Assert.Equal(TaskAudience.AdministratorOnly, begun.Audience);
        Assert.Equal(["accounts", "databases", "restore"], _tasks.Reports.Select(report => { return report.Line; }));
        Assert.All(_tasks.Reports, report => { Assert.InRange(report.Percent, 0, 99); });
        Assert.Equal(1, _tasks.Completed);
        Assert.Empty(_tasks.Failures);
    }

    /// <summary>A rolled-back import fails its task with the code that stopped it and never completes it.</summary>
    [Fact]
    public async Task A_rolled_back_import_fails_its_task_with_the_code_that_stopped_it()
    {
        var (orchestrator, _, importId) = Build(nameof(A_rolled_back_import_fails_its_task_with_the_code_that_stopped_it));
        _databases.FailWith = Error.Of("DatabaseNameTaken", ErrorType.Conflict);

        await orchestrator.RunAsync(importId, CancellationToken.None);

        Assert.Equal(["DatabaseNameTaken"], _tasks.Failures);
        Assert.Equal(0, _tasks.Completed);
    }
}
