using Maran.Agent.Client.Services.CronService;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Queries.ListCronEntries;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Queries.ListCronEntries;

/// <summary>Whose crontab a listing reads, and what it reports about it.</summary>
public sealed class ListCronEntriesQueryHandlerTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Another tenants account is answered not found and its crontab is never read.</summary>
    [Fact]
    public async Task Another_tenants_account_is_answered_not_found_and_its_crontab_is_never_read()
    {
        // The IDOR case for a read. It matters as much here as on a mutation: a listing returns the
        // customer's commands, which are the very values this module refuses to write to a log.
        var world = new World();

        var result = await world.HandleAsync(StrangerAccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Lists);
    }

    /// <summary>The listing reports what the agent read out of the crontab field for field.</summary>
    [Fact]
    public async Task The_listing_reports_what_the_agent_read_out_of_the_crontab_field_for_field()
    {
        // Including entries the panel never installed, and including a disabled one: the crontab is
        // the record, and a customer who edited it over SFTP must see what is actually there.
        var world = new World();
        world.Agent.ListEntriesResult = Result<IReadOnlyList<AgentCronEntry>>.Ok(
        [
            new AgentCronEntry(
                "aaaa1111-0000-4000-8000-000000000001",
                new AgentCronSchedule("0", "3", "*", "*", "1"),
                "/usr/bin/backup",
                true),
            new AgentCronEntry(
                "aaaa1111-0000-4000-8000-000000000002",
                new AgentCronSchedule("*/5", "*", "*", "*", "*"),
                "/usr/bin/poll",
                false),
        ]);

        var result = await world.HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("aaaa1111-0000-4000-8000-000000000001", result.Value[0].EntryId);
        Assert.Equal(new CronScheduleDto("0", "3", "*", "*", "1"), result.Value[0].Schedule);
        Assert.Equal("/usr/bin/backup", result.Value[0].Command);
        Assert.True(result.Value[0].Enabled);
        Assert.False(result.Value[1].Enabled);
        Assert.Equal(new CronScheduleDto("*/5", "*", "*", "*", "*"), result.Value[1].Schedule);
        Assert.All(result.Value, entry =>
        {
            Assert.Equal(AccountId, entry.AccountId);
        });
    }

    /// <summary>The command is returned to its owner in full.</summary>
    [Fact]
    public async Task The_command_is_returned_to_its_owner_in_full()
    {
        // The other half of RULING 31, and the half a later reader is most likely to "fix". The
        // command is kept out of every log line and out of the journal because those are read by the
        // operator; it goes back to the CUSTOMER unchanged, because they wrote it and they cannot
        // edit a job they are not allowed to read.
        const string Command = "/usr/bin/mysql -pHUNTER2SECRET --execute='call nightly()'";
        var world = new World();
        world.Agent.ListEntriesResult = Result<IReadOnlyList<AgentCronEntry>>.Ok(
        [
            new AgentCronEntry(
                "aaaa1111-0000-4000-8000-000000000001",
                new AgentCronSchedule("0", "3", "*", "*", "*"),
                Command,
                true),
        ]);

        var result = await world.HandleAsync();

        Assert.Equal(Command, Assert.Single(result.Value).Command);
    }

    /// <summary>A crontab the agent cannot read is a failure and never an empty listing.</summary>
    [Fact]
    public async Task A_crontab_the_agent_cannot_read_is_a_failure_and_never_an_empty_listing()
    {
        // An empty listing is a claim: "this account has no scheduled tasks". Making it from an
        // agent outage would tell a customer their jobs are gone.
        var world = new World();
        world.Agent.ListEntriesResult = Result<IReadOnlyList<AgentCronEntry>>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.HandleAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("CronOperationFailed", result.Error!.Code);
    }

    /// <summary>Everything one listing test needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The agent double every call is recorded on; it stands in for the crontab.</summary>
        public RecordingAgentCronClient Agent { get; } = new();

        /// <summary>Every line the handler logged, rendered as a sink would write it.</summary>
        public CapturingLogger<ListCronEntriesQueryHandler> Logger { get; } = new();

        /// <summary>The handler under test.</summary>
        public ListCronEntriesQueryHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        public World()
        {
            Handler = new ListCronEntriesQueryHandler(
                new StubAccountDirectory(new AccountSnapshot(AccountId, "alice", 5, 5, 5, 5, 5, 1_024)),
                Agent,
                Logger);
        }

        /// <summary>Runs the handler.</summary>
        /// <param name="accountId">The account whose crontab to read; the owner by default.</param>
        public async Task<Result<IReadOnlyList<CronEntryDto>>> HandleAsync(Guid? accountId = null)
        {
            return await Handler.HandleAsync(
                new ListCronEntriesQuery(accountId ?? AccountId), CancellationToken.None);
        }
    }
}
