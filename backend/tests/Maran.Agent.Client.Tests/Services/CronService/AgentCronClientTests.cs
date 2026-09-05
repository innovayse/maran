using Maran.Agent.Client.Services.CronService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.CronService;

/// <summary>Mapping contract of AgentCronClient (proto oneof to Result, and every request field).</summary>
public sealed class AgentCronClientTests
{
    /// <summary>The command every test that sends one uses.</summary>
    private const string BackupCommand = "/usr/bin/php /home/alice/backup.php";

    /// <summary>The schedule every test that sends one uses: 03:30 every day.</summary>
    private static readonly AgentCronSchedule NightlySchedule = new("30", "3", "*", "*", "*");

    /// <summary>Listing sends the account whose entries are wanted.</summary>
    [Fact]
    public async Task Listing_sends_the_account_whose_entries_are_wanted()
    {
        var stub = new StubCronService
        {
            ListResponse = new ListCronEntriesResponse { Ok = new ListCronEntriesOk() },
        };

        await Client(stub).ListEntriesAsync("alice", CancellationToken.None);

        var request = Assert.IsType<ListCronEntriesRequest>(stub.LastListRequest);
        Assert.Equal("alice", request.AccountUsername);
    }

    /// <summary>Listing ok payload maps the id the schedule the command and the enablement of every row.</summary>
    [Fact]
    public async Task Listing_ok_payload_maps_the_id_the_schedule_the_command_and_the_enablement_of_every_row()
    {
        var stub = new StubCronService
        {
            ListResponse = new ListCronEntriesResponse
            {
                Ok = new ListCronEntriesOk
                {
                    Entries =
                    {
                        new CronEntry
                        {
                            EntryId = "e1",
                            Schedule = new CronSchedule
                            {
                                Minute = "30",
                                Hour = "3",
                                DayOfMonth = "*",
                                Month = "*",
                                DayOfWeek = "1-5",
                            },
                            Command = BackupCommand,
                            Enabled = true,
                        },
                        new CronEntry
                        {
                            EntryId = "e2",
                            Schedule = new CronSchedule
                            {
                                Minute = "*/5",
                                Hour = "*",
                                DayOfMonth = "*",
                                Month = "*",
                                DayOfWeek = "*",
                            },
                            Command = "/usr/bin/true",
                            Enabled = false,
                        },
                    },
                },
            },
        };

        var result = await Client(stub).ListEntriesAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("e1", result.Value[0].EntryId);
        Assert.Equal(new AgentCronSchedule("30", "3", "*", "*", "1-5"), result.Value[0].Schedule);
        Assert.Equal(BackupCommand, result.Value[0].Command);
        Assert.True(result.Value[0].Enabled);
        Assert.Equal("e2", result.Value[1].EntryId);
        Assert.Equal(new AgentCronSchedule("*/5", "*", "*", "*", "*"), result.Value[1].Schedule);
        Assert.False(result.Value[1].Enabled);
    }

    /// <summary>A row whose schedule the agent did not send is refused rather than rendered as a time.</summary>
    /// <remarks>
    /// A schedule is a nested message, so proto3 lets it be absent, and there is no honest reading of
    /// an absent one: five empty fields render as an entry that runs at no time, and inventing
    /// <c>* * * * *</c> renders as one that runs every minute. Dropping the row would show a
    /// customer a crontab shorter than the one installed.
    /// </remarks>
    [Fact]
    public async Task A_row_whose_schedule_the_agent_did_not_send_is_refused_rather_than_rendered_as_a_time()
    {
        var stub = new StubCronService
        {
            ListResponse = new ListCronEntriesResponse
            {
                Ok = new ListCronEntriesOk
                {
                    Entries = { new CronEntry { EntryId = "e1", Command = BackupCommand } },
                },
            },
        };

        var result = await Client(stub).ListEntriesAsync("alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Listing error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task Listing_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = StubCronService.FailingListWith(ErrorCode.NotFound, "no such account");

        var result = await Client(stub).ListEntriesAsync("alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>The agents diagnostic text is logged and never carried back to the caller.</summary>
    [Fact]
    public async Task The_agents_diagnostic_text_is_logged_and_never_carried_back_to_the_caller()
    {
        var logger = new RecordingLogger<AgentCronClient>();
        var stub = StubCronService.FailingListWith(
            ErrorCode.SystemFailure,
            "cannot read /var/spool/cron/crontabs/alice");

        var result = await new AgentCronClient(stub, logger).ListEntriesAsync("alice", CancellationToken.None);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.DoesNotContain("/var/spool", result.Error.Code, StringComparison.Ordinal);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("/var/spool/cron/crontabs/alice", logged, StringComparison.Ordinal);
    }

    /// <summary>A listing response with neither branch set is refused rather than read as empty.</summary>
    [Fact]
    public async Task A_listing_response_with_neither_branch_set_is_refused_rather_than_read_as_empty()
    {
        var result = await Client(new StubCronService()).ListEntriesAsync("alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Creation sends the account the five schedule fields and the command.</summary>
    [Fact]
    public async Task Creation_sends_the_account_the_five_schedule_fields_and_the_command()
    {
        var stub = new StubCronService();

        await Client(stub).CreateEntryAsync("alice", NightlySchedule, BackupCommand, CancellationToken.None);

        var request = Assert.IsType<CreateCronEntryRequest>(stub.LastCreateRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("30", request.Schedule.Minute);
        Assert.Equal("3", request.Schedule.Hour);
        Assert.Equal("*", request.Schedule.DayOfMonth);
        Assert.Equal("*", request.Schedule.Month);
        Assert.Equal("*", request.Schedule.DayOfWeek);
        Assert.Equal(BackupCommand, request.Command);
    }

    /// <summary>Creation ok payload maps to the identifier the agent assigned.</summary>
    [Fact]
    public async Task Creation_ok_payload_maps_to_the_identifier_the_agent_assigned()
    {
        var stub = new StubCronService
        {
            CreateResponse = new CreateCronEntryResponse
            {
                Ok = new CreateCronEntryOk { EntryId = "e7" },
            },
        };

        var result = await Client(stub)
            .CreateEntryAsync("alice", NightlySchedule, BackupCommand, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("e7", result.Value);
    }

    /// <summary>Creation error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task Creation_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubCronService
        {
            CreateResponse = new CreateCronEntryResponse
            {
                Error = new AgentError { Code = ErrorCode.AlreadyExists, Message = "entry exists" },
            },
        };

        var result = await Client(stub)
            .CreateEntryAsync("alice", NightlySchedule, BackupCommand, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentAlreadyExists", result.Error!.Code);
    }

    /// <summary>A creation response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_creation_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await Client(new StubCronService())
            .CreateEntryAsync("alice", NightlySchedule, BackupCommand, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Update sends the entry the new schedule and the new command and sets no enablement.</summary>
    /// <remarks>
    /// The enablement field is deprecated and never read by the agent, and the panel surface has no
    /// parameter for it. What this can still catch is somebody setting it here later: an update that
    /// carried enablement would switch a disabled entry back on whenever a caller edited its command
    /// without thinking about the flag.
    /// </remarks>
    [Fact]
    public async Task Update_sends_the_entry_the_new_schedule_and_the_new_command_and_sets_no_enablement()
    {
        var stub = new StubCronService
        {
            UpdateResponse = new UpdateCronEntryResponse { Ok = new UpdateCronEntryOk() },
        };

        await Client(stub).UpdateEntryAsync("alice", "e1", NightlySchedule, "/usr/bin/true", CancellationToken.None);

        var request = Assert.IsType<UpdateCronEntryRequest>(stub.LastUpdateRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("e1", request.EntryId);
        Assert.Equal("30", request.Schedule.Minute);
        Assert.Equal("/usr/bin/true", request.Command);
        Assert.False(request.Enabled);
    }

    /// <summary>Update ok payload maps to success.</summary>
    [Fact]
    public async Task Update_ok_payload_maps_to_success()
    {
        var stub = new StubCronService
        {
            UpdateResponse = new UpdateCronEntryResponse { Ok = new UpdateCronEntryOk() },
        };

        var result = await Client(stub)
            .UpdateEntryAsync("alice", "e1", NightlySchedule, BackupCommand, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    /// <summary>Update error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task Update_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubCronService
        {
            UpdateResponse = new UpdateCronEntryResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no such entry" },
            },
        };

        var result = await Client(stub)
            .UpdateEntryAsync("alice", "e1", NightlySchedule, BackupCommand, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>An update response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task An_update_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await Client(new StubCronService())
            .UpdateEntryAsync("alice", "e1", NightlySchedule, BackupCommand, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Deletion sends the account and the entry.</summary>
    [Fact]
    public async Task Deletion_sends_the_account_and_the_entry()
    {
        var stub = new StubCronService
        {
            DeleteResponse = new DeleteCronEntryResponse { Ok = new DeleteCronEntryOk() },
        };

        await Client(stub).DeleteEntryAsync("alice", "e1", CancellationToken.None);

        var request = Assert.IsType<DeleteCronEntryRequest>(stub.LastDeleteRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("e1", request.EntryId);
    }

    /// <summary>Deletion ok payload maps to success.</summary>
    [Fact]
    public async Task Deletion_ok_payload_maps_to_success()
    {
        var stub = new StubCronService
        {
            DeleteResponse = new DeleteCronEntryResponse { Ok = new DeleteCronEntryOk() },
        };

        var result = await Client(stub).DeleteEntryAsync("alice", "e1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    /// <summary>Deletion error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task Deletion_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubCronService
        {
            DeleteResponse = new DeleteCronEntryResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no such entry" },
            },
        };

        var result = await Client(stub).DeleteEntryAsync("alice", "e1", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>A deletion response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_deletion_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await Client(new StubCronService()).DeleteEntryAsync("alice", "e1", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Switching an entry sends the flag the caller asked for and not the one it had.</summary>
    /// <param name="enabled">The state the caller asked for.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Switching_an_entry_sends_the_flag_the_caller_asked_for_and_not_the_one_it_had(bool enabled)
    {
        var stub = new StubCronService
        {
            SetEnabledResponse = new SetCronEntryEnabledResponse { Ok = new SetCronEntryEnabledOk() },
        };

        await Client(stub).SetEntryEnabledAsync("alice", "e1", enabled, CancellationToken.None);

        var request = Assert.IsType<SetCronEntryEnabledRequest>(stub.LastSetEnabledRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("e1", request.EntryId);
        Assert.Equal(enabled, request.Enabled);
    }

    /// <summary>The enablement ok payload maps to success.</summary>
    [Fact]
    public async Task The_enablement_ok_payload_maps_to_success()
    {
        var stub = new StubCronService
        {
            SetEnabledResponse = new SetCronEntryEnabledResponse { Ok = new SetCronEntryEnabledOk() },
        };

        var result = await Client(stub).SetEntryEnabledAsync("alice", "e1", true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    /// <summary>The enablement error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_enablement_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubCronService
        {
            SetEnabledResponse = new SetCronEntryEnabledResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no such entry" },
            },
        };

        var result = await Client(stub).SetEntryEnabledAsync("alice", "e1", true, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>An enablement response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task An_enablement_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await Client(new StubCronService())
            .SetEntryEnabledAsync("alice", "e1", true, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The output request sends the account and the entry.</summary>
    [Fact]
    public async Task The_output_request_sends_the_account_and_the_entry()
    {
        var stub = new StubCronService
        {
            OutputResponse = new GetCronEntryOutputResponse { Ok = new GetCronEntryOutputOk() },
        };

        await Client(stub).GetEntryOutputAsync("alice", "e1", CancellationToken.None);

        var request = Assert.IsType<GetCronEntryOutputRequest>(stub.LastOutputRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("e1", request.EntryId);
    }

    /// <summary>An entry that has never run comes back as no output at all and not as a successful run.</summary>
    /// <remarks>
    /// All three fields absent is the shape the agent sends for an entry with no output file. Reading
    /// the proto3 defaults instead would report a run that printed nothing, exited 0 and finished at
    /// the epoch — a green tick beside a job that has never fired.
    /// </remarks>
    [Fact]
    public async Task An_entry_that_has_never_run_comes_back_as_no_output_at_all_and_not_as_a_successful_run()
    {
        var stub = new StubCronService
        {
            OutputResponse = new GetCronEntryOutputResponse { Ok = new GetCronEntryOutputOk() },
        };

        var result = await Client(stub).GetEntryOutputAsync("alice", "e1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    /// <summary>A run that printed nothing is not the same answer as an entry that never ran.</summary>
    [Fact]
    public async Task A_run_that_printed_nothing_is_not_the_same_answer_as_an_entry_that_never_ran()
    {
        var stub = new StubCronService
        {
            OutputResponse = new GetCronEntryOutputResponse
            {
                Ok = new GetCronEntryOutputOk { Output = string.Empty, LastExitCode = 0 },
            },
        };

        var result = await Client(stub).GetEntryOutputAsync("alice", "e1", CancellationToken.None);

        var output = result.Value;
        Assert.NotNull(output);
        Assert.Equal(string.Empty, output.Output);
        Assert.Equal(0, output.LastExitCode);
        Assert.Null(output.LastRunAtUnix);
    }

    /// <summary>A run the agent measured carries its output its status and its finish time through.</summary>
    [Fact]
    public async Task A_run_the_agent_measured_carries_its_output_its_status_and_its_finish_time_through()
    {
        var stub = new StubCronService
        {
            OutputResponse = new GetCronEntryOutputResponse
            {
                Ok = new GetCronEntryOutputOk
                {
                    Output = "backup complete",
                    LastExitCode = 2,
                    LastRunAtUnix = 1_756_000_000,
                },
            },
        };

        var result = await Client(stub).GetEntryOutputAsync("alice", "e1", CancellationToken.None);

        var output = result.Value;
        Assert.NotNull(output);
        Assert.Equal("backup complete", output.Output);
        Assert.Equal(2, output.LastExitCode);
        Assert.Equal(1_756_000_000L, output.LastRunAtUnix);
    }

    /// <summary>The output error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_output_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubCronService
        {
            OutputResponse = new GetCronEntryOutputResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no such entry" },
            },
        };

        var result = await Client(stub).GetEntryOutputAsync("alice", "e1", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>An output response with neither branch set is refused rather than read as never run.</summary>
    [Fact]
    public async Task An_output_response_with_neither_branch_set_is_refused_rather_than_read_as_never_run()
    {
        var result = await Client(new StubCronService()).GetEntryOutputAsync("alice", "e1", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The environment read sends the account whose crontab is wanted.</summary>
    [Fact]
    public async Task The_environment_read_sends_the_account_whose_crontab_is_wanted()
    {
        var stub = new StubCronService
        {
            GetEnvironmentResponse = new GetCronEnvironmentResponse { Ok = new GetCronEnvironmentOk() },
        };

        await Client(stub).GetEnvironmentAsync("alice", CancellationToken.None);

        var request = Assert.IsType<GetCronEnvironmentRequest>(stub.LastGetEnvironmentRequest);
        Assert.Equal("alice", request.AccountUsername);
    }

    /// <summary>The environment ok payload keeps the assignments in the order the crontab holds them.</summary>
    [Fact]
    public async Task The_environment_ok_payload_keeps_the_assignments_in_the_order_the_crontab_holds_them()
    {
        var stub = new StubCronService
        {
            GetEnvironmentResponse = new GetCronEnvironmentResponse
            {
                Ok = new GetCronEnvironmentOk
                {
                    Variables =
                    {
                        new CronEnvironmentVariable { Name = "PATH", Value = "/usr/local/bin:/usr/bin" },
                        new CronEnvironmentVariable { Name = "TZ", Value = "Asia/Yerevan" },
                    },
                },
            },
        };

        var result = await Client(stub).GetEnvironmentAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(new AgentCronEnvVar("PATH", "/usr/local/bin:/usr/bin"), result.Value[0]);
        Assert.Equal(new AgentCronEnvVar("TZ", "Asia/Yerevan"), result.Value[1]);
    }

    /// <summary>The environment read error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_environment_read_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubCronService
        {
            GetEnvironmentResponse = new GetCronEnvironmentResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "crontab unreadable" },
            },
        };

        var result = await Client(stub).GetEnvironmentAsync("alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>An environment read with neither branch set is refused rather than read as empty.</summary>
    [Fact]
    public async Task An_environment_read_with_neither_branch_set_is_refused_rather_than_read_as_empty()
    {
        var result = await Client(new StubCronService()).GetEnvironmentAsync("alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The environment write sends every assignment the caller supplied.</summary>
    [Fact]
    public async Task The_environment_write_sends_every_assignment_the_caller_supplied()
    {
        var stub = new StubCronService
        {
            SetEnvironmentResponse = new SetCronEnvironmentResponse { Ok = new SetCronEnvironmentOk() },
        };

        await Client(stub).SetEnvironmentAsync(
            "alice",
            [new AgentCronEnvVar("PATH", "/usr/bin"), new AgentCronEnvVar("TZ", "UTC")],
            CancellationToken.None);

        var request = Assert.IsType<SetCronEnvironmentRequest>(stub.LastSetEnvironmentRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal(2, request.Variables.Count);
        Assert.Equal("PATH", request.Variables[0].Name);
        Assert.Equal("/usr/bin", request.Variables[0].Value);
        Assert.Equal("TZ", request.Variables[1].Name);
        Assert.Equal("UTC", request.Variables[1].Value);
    }

    /// <summary>An empty environment is sent as an empty set because that is how everything is cleared.</summary>
    /// <remarks>
    /// The write replaces the managed assignments whole, so an empty list is a request to remove them
    /// all — not a caller mistake to be refused, and not a no-op to be skipped.
    /// </remarks>
    [Fact]
    public async Task An_empty_environment_is_sent_as_an_empty_set_because_that_is_how_everything_is_cleared()
    {
        var stub = new StubCronService
        {
            SetEnvironmentResponse = new SetCronEnvironmentResponse { Ok = new SetCronEnvironmentOk() },
        };

        var result = await Client(stub).SetEnvironmentAsync("alice", [], CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.IsType<SetCronEnvironmentRequest>(stub.LastSetEnvironmentRequest);
        Assert.Empty(request.Variables);
    }

    /// <summary>The environment write error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_environment_write_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubCronService
        {
            SetEnvironmentResponse = new SetCronEnvironmentResponse
            {
                Error = new AgentError { Code = ErrorCode.InvalidInput, Message = "MAILTO is refused" },
            },
        };

        var result = await Client(stub).SetEnvironmentAsync(
            "alice",
            [new AgentCronEnvVar("MAILTO", "a@b.c")],
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidInput", result.Error!.Code);
    }

    /// <summary>An environment write with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task An_environment_write_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await Client(new StubCronService()).SetEnvironmentAsync("alice", [], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Builds the production client over a stub transport and a logger nothing asserts.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>The client under test.</returns>
    private static AgentCronClient Client(StubCronService stub)
    {
        return new AgentCronClient(stub, NullLogger<AgentCronClient>.Instance);
    }
}
