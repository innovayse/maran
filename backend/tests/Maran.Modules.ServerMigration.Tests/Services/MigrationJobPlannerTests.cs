using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.ServerMigration.Tests.Services;

/// <summary>The dry run of a whole job: conflicts, no rename, and nothing written (issue #197, phase 2).</summary>
public sealed class MigrationJobPlannerTests
{
    /// <summary>An importer that reports a conflict for the account names it is told are taken.</summary>
    private sealed class TakenNames : IAccountMigrationImporter
    {
        /// <summary>The names already used on this server.</summary>
        private readonly HashSet<string> _taken;

        /// <summary>Creates the importer.</summary>
        /// <param name="taken">The names already used.</param>
        public TakenNames(params string[] taken)
        {
            _taken = [.. taken];
        }

        /// <summary>How many times the importer was asked to import; a dry run asks for none.</summary>
        public int Imports { get; private set; }

        /// <inheritdoc/>
        public string ModuleId
        {
            get { return "accounts"; }
        }

        /// <inheritdoc/>
        public int Order
        {
            get { return 0; }
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<MigrationFinding>> PlanAsync(MigrationSection section, MigrationPlanContext context, CancellationToken cancellationToken)
        {
            IReadOnlyList<MigrationFinding> findings = _taken.Contains(context.AccountName)
                ? [new MigrationFinding("accounts", MigrationFindingSeverity.Conflict, MigrationCodes.NameTaken, context.AccountName)]
                : [];

            return Task.FromResult(findings);
        }

        /// <inheritdoc/>
        public Task<Result<MigrationSectionOutcome>> ImportAsync(MigrationSection section, MigrationImportTarget target, CancellationToken cancellationToken)
        {
            Imports++;

            return Task.FromResult(Result<MigrationSectionOutcome>.Ok(new MigrationSectionOutcome(null, [])));
        }
    }

    /// <summary>A mapped account named <paramref name="name"/>.</summary>
    /// <param name="name">The account.</param>
    /// <param name="domain">Its primary domain.</param>
    /// <param name="mapped">Whether the plan and owner are chosen.</param>
    /// <returns>The account.</returns>
    private static MigrationJobAccount Account(string name, string domain, bool mapped = true)
    {
        var account = new MigrationJobAccount(
            Guid.NewGuid(), Guid.NewGuid(), name, "/home/" + name, 10, 0, 0,
            MigrationSectionMapper.WriteInventory(new MigrationInventorySnapshot([], [])), domain);
        if (mapped)
        {
            account.Remap(Guid.NewGuid(), null, false, name + "@example.test", null, null, null);
        }

        return account;
    }

    /// <summary>A taken name is reported as a conflict on that account, and a free one is not (the inverse).</summary>
    [Fact]
    public async Task A_taken_name_is_a_conflict_on_its_account_and_a_free_one_is_clean()
    {
        var planner = new MigrationJobPlanner([new TakenNames("alice")]);

        var findings = await planner.PlanAsync([Account("alice", "a.example.test"), Account("bob", "b.example.test")], CancellationToken.None);

        var conflict = Assert.Single(findings);
        Assert.Equal("alice", conflict.SourceAccountName);
        Assert.Equal(MigrationCodes.NameTaken, conflict.Finding.Code);
    }

    /// <summary>The dry run imports nothing.</summary>
    [Fact]
    public async Task The_dry_run_never_imports()
    {
        var importer = new TakenNames();
        var planner = new MigrationJobPlanner([importer]);

        await planner.PlanAsync([Account("alice", "a.example.test")], CancellationToken.None);

        Assert.Equal(0, importer.Imports);
    }

    /// <summary>An account with no plan or owner is a conflict before any importer is asked.</summary>
    [Fact]
    public async Task An_unmapped_account_is_a_conflict()
    {
        var planner = new MigrationJobPlanner([new TakenNames()]);

        var findings = await planner.PlanAsync([Account("alice", "a.example.test", mapped: false)], CancellationToken.None);

        Assert.Equal("MigrationMappingIncomplete", Assert.Single(findings).Finding.Code);
    }

    /// <summary>Two accounts of the job claiming one domain are both told so.</summary>
    [Fact]
    public async Task Two_accounts_claiming_one_domain_are_both_in_conflict()
    {
        var planner = new MigrationJobPlanner([new TakenNames()]);

        var findings = await planner.PlanAsync([Account("alice", "same.example.test"), Account("bob", "same.example.test")], CancellationToken.None);

        Assert.Equal(["alice", "bob"], findings.Where(row =>
        {
            return row.Finding.Code == "MigrationDuplicateInJob";
        }).Select(row =>
        {
            return row.SourceAccountName;
        }).Order().ToArray());
    }

    /// <summary>Conflicts are listed before warnings so the screen shows what blocks the start first.</summary>
    [Fact]
    public async Task Conflicts_come_before_warnings()
    {
        var withCron = new MigrationJobAccount(
            Guid.NewGuid(), Guid.NewGuid(), "alice", "/home/alice", 10, 4, 0,
            MigrationSectionMapper.WriteInventory(new MigrationInventorySnapshot([], [])), "a.example.test");
        withCron.Remap(Guid.NewGuid(), null, false, "alice@example.test", null, null, null);
        var planner = new MigrationJobPlanner([new TakenNames("alice")]);

        var findings = await planner.PlanAsync([withCron], CancellationToken.None);

        Assert.Equal(MigrationFindingSeverity.Conflict, findings[0].Finding.Severity);
        Assert.Contains(findings, row =>
        {
            return row.Finding.Severity == MigrationFindingSeverity.Warning;
        });
    }
}
