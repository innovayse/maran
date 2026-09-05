using Maran.Modules.Cron.Commands.SetCronEntryEnabled;
using Maran.Modules.Cron.Services;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Commands.SetCronEntryEnabled;

/// <summary>What switching a cron entry on or off does, and what it records.</summary>
public sealed class SetCronEntryEnabledCommandHandlerTests
{
    private const string EntryId = "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>An entry under an account the caller may not see is answered not found and never forbidden.</summary>
    [Fact]
    public async Task An_entry_under_an_account_the_caller_may_not_see_is_answered_not_found_and_never_forbidden()
    {
        var world = new World();

        var result = await world.HandleAsync(accountId: StrangerAccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.EnabledChanges);
    }

    /// <summary>The state asked for is the state sent to the agent.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_state_asked_for_is_the_state_sent_to_the_agent(bool enabled)
    {
        // Both directions, because a handler that ignored the flag and always sent one value would
        // pass a test that only ever asked for the other.
        var world = new World();

        var result = await world.HandleAsync(enabled);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(world.Agent.EnabledChanges);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal(EntryId, call.EntryId);
        Assert.Equal(enabled, call.Enabled);
    }

    /// <summary>Enabling an entry is not counted against the plan allowance.</summary>
    [Fact]
    public async Task Enabling_an_entry_is_not_counted_against_the_plan_allowance()
    {
        // A disabled entry still occupies a crontab line and is still returned by the listing the
        // creation path counts, so it has already been charged for. Counting it again here would
        // leave a customer at their limit unable to turn their own jobs back on.
        var world = new World(maxCronEntries: 0);

        var result = await world.HandleAsync(enabled: true);

        Assert.True(result.IsSuccess);
        Assert.Empty(world.Agent.Lists);
    }

    /// <summary>An enablement change is journalled under its own action with the entry id.</summary>
    [Fact]
    public async Task An_enablement_change_is_journalled_under_its_own_action_with_the_entry_id()
    {
        // Its own action rather than an update's, because a disabled entry that still fires — or an
        // enabled one that does not — is a failure an operator has to be able to DATE, and folding
        // the flag into an edit would date it by an unrelated change.
        var world = new World();

        await world.HandleAsync(enabled: false);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEntryEnabledChanged, entry.Action);
        Assert.Equal(EntryId, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refused enablement change is journalled as a failure.</summary>
    [Fact]
    public async Task A_refused_enablement_change_is_journalled_as_a_failure()
    {
        var world = new World();
        world.Agent.SetEntryEnabledResult = Result<bool>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await world.HandleAsync();

        Assert.Equal("CronEntryNotFound", result.Error!.Code);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEntryEnabledChanged, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Everything one enablement test needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The agent double every call is recorded on; it stands in for the crontab.</summary>
        public RecordingAgentCronClient Agent { get; } = new();

        /// <summary>The journal every entry lands in.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Every line the handler logged, rendered as a sink would write it.</summary>
        public CapturingLogger<SetCronEntryEnabledCommandHandler> Logger { get; } = new();

        /// <summary>The handler under test.</summary>
        public SetCronEntryEnabledCommandHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        /// <param name="maxCronEntries">The plan allowance the stubbed account carries.</param>
        public World(int maxCronEntries = 5)
        {
            var currentUser = FakeCurrentUser.Customer(AccountId);
            Handler = new SetCronEntryEnabledCommandHandler(
                new StubAccountDirectory(new AccountSnapshot(AccountId, "alice", 5, 5, 5, maxCronEntries, 5, 1_024)),
                Agent,
                new CronAuditJournal(Audit, currentUser),
                Logger);
        }

        /// <summary>Runs the handler with the usual audit context.</summary>
        /// <param name="enabled">The state to ask for.</param>
        /// <param name="accountId">The account to act under; the owner by default.</param>
        public async Task<Result<bool>> HandleAsync(bool enabled = true, Guid? accountId = null)
        {
            return await Handler.HandleAsync(
                new SetCronEntryEnabledCommand(
                    accountId ?? AccountId, EntryId, enabled, "203.0.113.7", "tests"),
                CancellationToken.None);
        }
    }
}
