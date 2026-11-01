using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Queries.GetCronRun;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Queries.GetCronRun;

/// <summary>Reading one run and its output, and the addresses that must not find it.</summary>
public sealed class GetCronRunQueryHandlerTests
{
    private const string EntryId = "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";
    private const string OtherEntryId = "9f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>A run is read with what it printed.</summary>
    [Fact]
    public async Task A_run_is_read_with_what_it_printed()
    {
        var (handler, run) = await SeededAsync();

        var result = await handler.HandleAsync(new GetCronRunQuery(AccountId, EntryId, run.Id), default);

        Assert.Equal("out", result.Value.Stdout);
        Assert.Equal("err", result.Value.Stderr);
    }

    /// <summary>A run id under another entry answers not found.</summary>
    [Fact]
    public async Task A_run_id_under_another_entry_is_not_found()
    {
        var (handler, run) = await SeededAsync();

        var result = await handler.HandleAsync(new GetCronRunQuery(AccountId, OtherEntryId, run.Id), default);

        Assert.Equal("CronRunNotFound", result.Error!.Code);
    }

    /// <summary>A run under an account the caller may not see answers not found, never forbidden.</summary>
    [Fact]
    public async Task A_run_under_an_account_the_caller_may_not_see_is_not_found()
    {
        var (handler, run) = await SeededAsync();

        var result = await handler.HandleAsync(new GetCronRunQuery(StrangerAccountId, EntryId, run.Id), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>A handler over a database holding one run of the owner's entry.</summary>
    /// <returns>The handler and the run.</returns>
    private static async Task<(GetCronRunQueryHandler Handler, CronRun Run)> SeededAsync()
    {
        var database = InMemoryCronDatabase.Open(Guid.NewGuid().ToString(), FakeCurrentUser.Customer(AccountId));
        var run = new CronRun(Guid.NewGuid(), AccountId, EntryId, DateTimeOffset.UnixEpoch, 10, 0, null, "out", "err");
        database.CronRuns.Add(run);
        await database.SaveChangesAsync();

        var handler = new GetCronRunQueryHandler(
            database,
            new StubAccountDirectory(new AccountSnapshot(AccountId, "alice", 5, 5, 5, 5, 5, 1_024, AccountState.Active)));

        return (handler, run);
    }
}
