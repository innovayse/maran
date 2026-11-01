using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Persistence;
using Maran.Modules.Cron.Queries.ListCronRuns;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Cron.Tests.Queries.ListCronRuns;

/// <summary>What one page of an entry's run history holds, and what it refuses.</summary>
/// <remarks>
/// The search is an <c>ILIKE</c> the in-memory provider cannot evaluate, so it is asserted by the
/// endpoint test against PostgreSQL (<c>CronRunsEndpointTests</c>), not here.
/// </remarks>
public sealed class ListCronRunsQueryHandlerTests
{
    private const string EntryId = "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";
    private const string OtherEntryId = "9f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>An account the caller may not see is answered not found.</summary>
    [Fact]
    public async Task An_account_the_caller_may_not_see_is_not_found()
    {
        var handler = Handler(out _);

        var result = await handler.HandleAsync(new ListCronRunsQuery(StrangerAccountId, EntryId: EntryId), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
    }

    /// <summary>The page holds only this entry's runs, newest first.</summary>
    [Fact]
    public async Task The_page_holds_only_this_entrys_runs_newest_first()
    {
        var handler = Handler(out var database);
        database.CronRuns.AddRange(
            Run(EntryId, Noon.AddMinutes(-2), 0, null),
            Run(EntryId, Noon, 0, null),
            Run(OtherEntryId, Noon.AddMinutes(1), 0, null));
        await database.SaveChangesAsync();

        var result = await handler.HandleAsync(new ListCronRunsQuery(AccountId, EntryId: EntryId), default);

        Assert.Equal(2, result.Value.Total);
        Assert.Equal([Noon, Noon.AddMinutes(-2)], result.Value.Items.Select(run => run.StartedAt));
    }

    /// <summary>The outcome filter keeps only runs that ended that way.</summary>
    [Fact]
    public async Task The_outcome_filter_keeps_only_runs_that_ended_that_way()
    {
        var handler = Handler(out var database);
        database.CronRuns.AddRange(
            Run(EntryId, Noon, 0, null),
            Run(EntryId, Noon.AddMinutes(1), 2, null),
            Run(EntryId, Noon.AddMinutes(2), null, CronRunLimit.Output));
        await database.SaveChangesAsync();

        var result = await handler.HandleAsync(
            new ListCronRunsQuery(AccountId, Outcome: CronRunOutcome.Failed, EntryId: EntryId),
            default);

        var row = Assert.Single(result.Value.Items);
        Assert.Equal(2, row.ExitCode);
    }

    /// <summary>A page of the given size is cut from the newest.</summary>
    [Fact]
    public async Task A_page_of_the_given_size_is_cut_from_the_newest()
    {
        var handler = Handler(out var database);
        for (var minute = 0; minute < 12; minute++)
        {
            database.CronRuns.Add(Run(EntryId, Noon.AddMinutes(minute), 0, null));
        }

        await database.SaveChangesAsync();

        var result = await handler.HandleAsync(
            new ListCronRunsQuery(AccountId, Page: 2, PageSize: 10, EntryId: EntryId),
            default);

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(2, result.Value.Items.Count);
    }

    /// <summary>The handler over a fresh database whose owner is the caller.</summary>
    /// <param name="database">The database the handler reads, for seeding.</param>
    /// <returns>The handler.</returns>
    private static ListCronRunsQueryHandler Handler(out CronDbContext database)
    {
        database = InMemoryCronDatabase.Open(Guid.NewGuid().ToString(), FakeCurrentUser.Customer(AccountId));
        return new ListCronRunsQueryHandler(
            database,
            new StubAccountDirectory(new AccountSnapshot(AccountId, "alice", 5, 5, 5, 5, 5, 1_024, AccountState.Active)));
    }

    /// <summary>A recorded run.</summary>
    /// <param name="entryId">The entry.</param>
    /// <param name="startedAt">When it started.</param>
    /// <param name="exitCode">Its status.</param>
    /// <param name="limit">The ceiling that stopped it.</param>
    /// <returns>The run.</returns>
    private static CronRun Run(string entryId, DateTimeOffset startedAt, int? exitCode, CronRunLimit? limit)
    {
        return new CronRun(Guid.NewGuid(), AccountId, entryId, startedAt, 10, exitCode, limit, "out", "err");
    }
}
