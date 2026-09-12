using Maran.Agent.Client.Services.FtpsService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.FtpsService;

/// <summary>Mapping contract of AgentFtpsClient (proto oneof to Result, and every request field).</summary>
public sealed class AgentFtpsClientTests
{
    /// <summary>The password a creation call carries, long enough to be a real generated one.</summary>
    private const string GeneratedPassword = "Qm4-brisk-otter-91";

    /// <summary>The password a change call carries, distinct from the creation one.</summary>
    private const string ReplacementPassword = "Vd2-amber-heron-77";

    /// <summary>Lowest passive port under test; different from the highest so a swap is visible.</summary>
    private const uint PassiveMin = 30000;

    /// <summary>Highest passive port under test.</summary>
    private const uint PassiveMax = 30100;

    /// <summary>The six boolean facts of a status answer, in the order the contract declares them.</summary>
    /// <returns>One theory row per flag position.</returns>
    /// <remarks>
    /// Read one flag at a time rather than nine at once, because a mapper that routed
    /// <c>certificate_present</c> into <c>Running</c> would satisfy any assertion that only checked
    /// a mixed pattern where both happened to be true. Here exactly one flag is set on the wire and
    /// exactly one is asserted true in the result, so every misrouting fails on its own row.
    /// </remarks>
    public static TheoryData<int> StatusFlagPositions()
    {
        return [0, 1, 2, 3, 4, 5];
    }

    /// <summary>Enable sends every field of the configuration to the agent.</summary>
    /// <remarks>
    /// Every field, asserted: request mapping that no test reads is the shape that survives a
    /// mutation pass unnoticed, and an unsent passive range is a daemon whose data ports do not
    /// match the ones the panel opened in the firewall.
    /// </remarks>
    [Fact]
    public async Task Enable_sends_every_field_of_the_configuration()
    {
        var stub = new StubFtpsService();

        await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .EnableAsync("ftp.example.test", PassiveMin, PassiveMax, "203.0.113.7", 42, CancellationToken.None);

        var request = Assert.IsType<EnableFtpsRequest>(stub.LastEnableRequest);
        Assert.Equal("ftp.example.test", request.Hostname);
        Assert.Equal(PassiveMin, request.PassivePortMin);
        Assert.Equal(PassiveMax, request.PassivePortMax);
        Assert.Equal("203.0.113.7", request.PassiveAddress);
        Assert.Equal(42u, request.MaxClients);
    }

    /// <summary>The enable ok payload maps every observed fact onto the status shape.</summary>
    [Fact]
    public async Task The_enable_ok_payload_maps_every_observed_fact_onto_the_status_shape()
    {
        var stub = new StubFtpsService
        {
            EnableResponse = new EnableFtpsResponse
            {
                Ok = new EnableFtpsOk
                {
                    Running = true,
                    ControlPortAnswered = false,
                    CertificatePresent = true,
                    CertificateIsSelfSigned = false,
                    CertificatePath = "/etc/maran/ftps/ftp.example.test",
                    PassivePortMin = PassiveMin,
                    PassivePortMax = PassiveMax,
                    Ipv4Only = true,
                    ForcedTls = false,
                },
            },
        };

        var result = await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .EnableAsync("ftp.example.test", PassiveMin, PassiveMax, string.Empty, 42, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new FtpsStatusDto(
                true,
                false,
                true,
                false,
                "/etc/maran/ftps/ftp.example.test",
                PassiveMin,
                PassiveMax,
                true,
                false),
            result.Value);
    }

    /// <summary>The enable error payload maps to a failed result with the agent code and no agent text.</summary>
    [Fact]
    public async Task The_enable_error_payload_maps_to_a_failed_result_with_the_agent_code_and_no_agent_text()
    {
        var logger = new RecordingLogger<AgentFtpsClient>();
        var stub = new StubFtpsService
        {
            EnableResponse = new EnableFtpsResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.NotFound,
                    Message = "no certificate material at /etc/maran/ftps/ftp.example.test",
                },
            },
        };

        var result = await new AgentFtpsClient(stub, logger)
            .EnableAsync("ftp.example.test", PassiveMin, PassiveMax, string.Empty, 42, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
        Assert.DoesNotContain("/etc/maran", result.Error.Code, StringComparison.Ordinal);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("/etc/maran/ftps/ftp.example.test", logged, StringComparison.Ordinal);
    }

    /// <summary>An enable response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task An_enable_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await new AgentFtpsClient(new StubFtpsService(), NullLogger<AgentFtpsClient>.Instance)
            .EnableAsync("ftp.example.test", PassiveMin, PassiveMax, string.Empty, 42, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Disable sends the empty request and maps its observation onto the status shape.</summary>
    [Fact]
    public async Task Disable_sends_the_empty_request_and_maps_its_observation_onto_the_status_shape()
    {
        var stub = new StubFtpsService
        {
            DisableResponse = new DisableFtpsResponse
            {
                Ok = new DisableFtpsOk
                {
                    Running = false,
                    ControlPortAnswered = false,
                    CertificatePresent = true,
                    CertificateIsSelfSigned = true,
                    CertificatePath = "/etc/maran/ftps/ftp.example.test",
                    PassivePortMin = PassiveMin,
                    PassivePortMax = PassiveMax,
                    Ipv4Only = false,
                    ForcedTls = true,
                },
            },
        };

        var result = await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .DisableAsync(CancellationToken.None);

        Assert.NotNull(stub.LastDisableRequest);
        Assert.True(result.IsSuccess);
        Assert.Equal(
            new FtpsStatusDto(
                false,
                false,
                true,
                true,
                "/etc/maran/ftps/ftp.example.test",
                PassiveMin,
                PassiveMax,
                false,
                true),
            result.Value);
    }

    /// <summary>The disable error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_disable_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubFtpsService
        {
            DisableResponse = new DisableFtpsResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "systemctl refused" },
            },
        };

        var result = await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .DisableAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A disable response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_disable_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await new AgentFtpsClient(new StubFtpsService(), NullLogger<AgentFtpsClient>.Instance)
            .DisableAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The status call sends the hostname it was asked about, the empty one included.</summary>
    /// <remarks>
    /// Both values, because an empty hostname is a MEANING in this contract — "report the daemon and
    /// range facts only" — and a client that substituted a default would turn a panel with no
    /// hostname persisted yet into a caller asking about the wrong name.
    /// </remarks>
    /// <param name="hostname">The hostname under test.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData("ftp.example.test")]
    [InlineData("")]
    public async Task The_status_call_sends_the_hostname_it_was_asked_about(string hostname)
    {
        var stub = new StubFtpsService();

        await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .GetStatusAsync(hostname, CancellationToken.None);

        var request = Assert.IsType<GetFtpsStatusRequest>(stub.LastStatusRequest);
        Assert.Equal(hostname, request.Hostname);
    }

    /// <summary>Each observed flag lands on its own member of the status shape and on no other.</summary>
    /// <param name="position">Which of the six boolean facts the agent reported as true.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(StatusFlagPositions))]
    public async Task Each_observed_flag_lands_on_its_own_member_of_the_status_shape(int position)
    {
        var ok = new GetFtpsStatusOk
        {
            Running = position == 0,
            ControlPortAnswered = position == 1,
            CertificatePresent = position == 2,
            CertificateIsSelfSigned = position == 3,
            Ipv4Only = position == 4,
            ForcedTls = position == 5,
        };
        var stub = new StubFtpsService
        {
            StatusResponse = new GetFtpsStatusResponse { Ok = ok },
        };

        var result = await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .GetStatusAsync("ftp.example.test", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var status = result.Value!;
        bool[] flags =
        [
            status.Running,
            status.ControlPortAnswered,
            status.CertificatePresent,
            status.CertificateIsSelfSigned,
            status.Ipv4Only,
            status.ForcedTls,
        ];
        Assert.Equal(1, flags.Count(flag => { return flag; }));
        Assert.True(flags[position]);
    }

    /// <summary>The status ok payload maps the path and both ends of the passive range.</summary>
    [Fact]
    public async Task The_status_ok_payload_maps_the_path_and_both_ends_of_the_passive_range()
    {
        var stub = new StubFtpsService
        {
            StatusResponse = new GetFtpsStatusResponse
            {
                Ok = new GetFtpsStatusOk
                {
                    CertificatePath = "/etc/maran/ftps/ftp.example.test",
                    PassivePortMin = PassiveMin,
                    PassivePortMax = PassiveMax,
                },
            },
        };

        var result = await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .GetStatusAsync("ftp.example.test", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/etc/maran/ftps/ftp.example.test", result.Value!.CertificatePath);
        Assert.Equal(PassiveMin, result.Value.PassivePortMin);
        Assert.Equal(PassiveMax, result.Value.PassivePortMax);
    }

    /// <summary>The status error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_status_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubFtpsService
        {
            StatusResponse = new GetFtpsStatusResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "cannot read the live configuration" },
            },
        };

        var result = await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .GetStatusAsync("ftp.example.test", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A status response with neither branch set is refused rather than read as success.</summary>
    /// <remarks>
    /// The direction matters here more than anywhere else in this file: an empty response read as
    /// success would hand the panel a status whose every flag is false, which includes
    /// <c>ForcedTls = false</c> — a screen saying "encryption is not enforced" about a daemon nobody
    /// observed. A refusal says the observation failed, which is the true statement.
    /// </remarks>
    [Fact]
    public async Task A_status_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await new AgentFtpsClient(new StubFtpsService(), NullLogger<AgentFtpsClient>.Instance)
            .GetStatusAsync("ftp.example.test", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The TLS reload sends the empty request and maps its observation onto the status shape.</summary>
    [Fact]
    public async Task The_tls_reload_sends_the_empty_request_and_maps_its_observation_onto_the_status_shape()
    {
        var stub = new StubFtpsService
        {
            ReloadResponse = new ReloadFtpsTlsResponse
            {
                Ok = new ReloadFtpsTlsOk
                {
                    Running = true,
                    ControlPortAnswered = true,
                    CertificatePresent = true,
                    CertificateIsSelfSigned = false,
                    CertificatePath = "/etc/maran/ftps/ftp.example.test",
                    PassivePortMin = PassiveMin,
                    PassivePortMax = PassiveMax,
                    Ipv4Only = false,
                    ForcedTls = true,
                },
            },
        };

        var result = await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .ReloadTlsAsync(CancellationToken.None);

        Assert.NotNull(stub.LastReloadRequest);
        Assert.True(result.IsSuccess);
        Assert.Equal(
            new FtpsStatusDto(
                true,
                true,
                true,
                false,
                "/etc/maran/ftps/ftp.example.test",
                PassiveMin,
                PassiveMax,
                false,
                true),
            result.Value);
    }

    /// <summary>The TLS reload error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_tls_reload_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubFtpsService
        {
            ReloadResponse = new ReloadFtpsTlsResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "restart refused" },
            },
        };

        var result = await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .ReloadTlsAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A TLS reload response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_tls_reload_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await new AgentFtpsClient(new StubFtpsService(), NullLogger<AgentFtpsClient>.Instance)
            .ReloadTlsAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Creation sends the account the login suffix and the password.</summary>
    [Fact]
    public async Task Creation_sends_the_account_the_login_suffix_and_the_password()
    {
        var stub = new StubFtpsService();

        await CreateAsync(stub, NullLogger<AgentFtpsClient>.Instance);

        var request = Assert.IsType<CreateFtpsUserRequest>(stub.LastCreateRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("files", request.FtpsUsername);
        Assert.Equal(GeneratedPassword, request.Password);
    }

    /// <summary>Creation ok payload maps to the fully qualified login the agent created.</summary>
    [Fact]
    public async Task Creation_ok_payload_maps_to_the_fully_qualified_login_the_agent_created()
    {
        var stub = new StubFtpsService
        {
            CreateResponse = new CreateFtpsUserResponse
            {
                Ok = new CreateFtpsUserOk { FtpsUsername = "acc1_files" },
            },
        };

        var result = await CreateAsync(stub, NullLogger<AgentFtpsClient>.Instance);

        Assert.True(result.IsSuccess);
        Assert.Equal("acc1_files", result.Value);
    }

    /// <summary>Creation error payload maps to a failed result with the agent code.</summary>
    /// <remarks>
    /// <c>AgentAlreadyExists</c> specifically, because the agent does NOT reset an existing login's
    /// password: a retried creation whose first response was lost must not invalidate the credential
    /// the customer was already shown.
    /// </remarks>
    [Fact]
    public async Task Creation_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = StubFtpsService.FailingCreateWith(ErrorCode.AlreadyExists, "login exists");

        var result = await CreateAsync(stub, NullLogger<AgentFtpsClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentAlreadyExists", result.Error!.Code);
    }

    /// <summary>A creation response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_creation_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await CreateAsync(new StubFtpsService(), NullLogger<AgentFtpsClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The generated password is never written to the log even when the agent quotes it back.</summary>
    /// <remarks>
    /// The realistic leak in this area, asserted as it actually happens: not a "using password: YES"
    /// line, but the daemon or PAM echoing the credential it refused. The surrounding words are
    /// asserted to survive, which is the positive control — a redaction that swallowed the whole
    /// sentence would satisfy the absence check and leave an operator with nothing to read.
    /// </remarks>
    [Fact]
    public async Task The_generated_password_is_never_written_to_the_log_even_when_the_agent_quotes_it_back()
    {
        var logger = new RecordingLogger<AgentFtpsClient>();
        var stub = StubFtpsService.FailingCreateWith(
            ErrorCode.SystemFailure,
            $"chpasswd: line 1: 'alice_files:{GeneratedPassword}' rejected by pam_pwquality");

        var result = await CreateAsync(stub, logger);

        Assert.Equal(GeneratedPassword, stub.LastCreateRequest!.Password);
        Assert.False(result.IsSuccess);
        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain(GeneratedPassword, logged, StringComparison.Ordinal);
        Assert.Contains("rejected by pam_pwquality", logged, StringComparison.Ordinal);
        Assert.Contains("alice_files", logged, StringComparison.Ordinal);
    }

    /// <summary>The password change sends the account the login suffix and the new password.</summary>
    /// <remarks>
    /// The account is asserted and not merely passed: the agent checks the login's jail against it
    /// before writing, so a client that dropped it would turn a request authorised for one tenant
    /// into one the agent can no longer refuse for another.
    /// </remarks>
    [Fact]
    public async Task The_password_change_sends_the_account_the_login_suffix_and_the_new_password()
    {
        var stub = new StubFtpsService
        {
            SetPasswordResponse = new SetFtpsPasswordResponse { Ok = new SetFtpsPasswordOk() },
        };

        await SetPasswordAsync(stub, NullLogger<AgentFtpsClient>.Instance);

        var request = Assert.IsType<SetFtpsPasswordRequest>(stub.LastSetPasswordRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("files", request.FtpsUsername);
        Assert.Equal(ReplacementPassword, request.Password);
    }

    /// <summary>The password change ok payload maps to success.</summary>
    [Fact]
    public async Task The_password_change_ok_payload_maps_to_success()
    {
        var stub = new StubFtpsService
        {
            SetPasswordResponse = new SetFtpsPasswordResponse { Ok = new SetFtpsPasswordOk() },
        };

        var result = await SetPasswordAsync(stub, NullLogger<AgentFtpsClient>.Instance);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    /// <summary>The password change error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_password_change_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = StubFtpsService.FailingSetPasswordWith(ErrorCode.NotFound, "chpasswd: user does not exist");

        var result = await SetPasswordAsync(stub, NullLogger<AgentFtpsClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>The replacement password is stripped from the agents tool output before it is logged.</summary>
    /// <remarks>
    /// The change rpc carries a secret exactly as creation does, so it must hand the same secret to
    /// the redaction. Asserted on tool output rather than on the message because that is where a
    /// password-setting tool actually echoes its input, and the login name is asserted present as
    /// the positive control.
    /// </remarks>
    [Fact]
    public async Task The_replacement_password_is_stripped_from_the_agents_tool_output_before_it_is_logged()
    {
        var logger = new RecordingLogger<AgentFtpsClient>();
        var stub = StubFtpsService.FailingSetPasswordWith(
            ErrorCode.SystemFailure,
            $"chpasswd: failed to set '{ReplacementPassword}' for alice_files");

        var result = await SetPasswordAsync(stub, logger);

        Assert.False(result.IsSuccess);
        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain(ReplacementPassword, logged, StringComparison.Ordinal);
        Assert.Contains("for alice_files", logged, StringComparison.Ordinal);
    }

    /// <summary>A password change response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_password_change_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await SetPasswordAsync(new StubFtpsService(), NullLogger<AgentFtpsClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Deletion sends the account and the login suffix and nothing else.</summary>
    [Fact]
    public async Task Deletion_sends_the_account_and_the_login_suffix_and_nothing_else()
    {
        var stub = new StubFtpsService
        {
            DeleteResponse = new DeleteFtpsUserResponse { Ok = new DeleteFtpsUserOk() },
        };

        await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .DeleteUserAsync("alice", "files", CancellationToken.None);

        var request = Assert.IsType<DeleteFtpsUserRequest>(stub.LastDeleteRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("files", request.FtpsUsername);
    }

    /// <summary>Deletion ok payload maps to success.</summary>
    [Fact]
    public async Task Deletion_ok_payload_maps_to_success()
    {
        var stub = new StubFtpsService
        {
            DeleteResponse = new DeleteFtpsUserResponse { Ok = new DeleteFtpsUserOk() },
        };

        var result = await new AgentFtpsClient(stub, NullLogger<AgentFtpsClient>.Instance)
            .DeleteUserAsync("alice", "files", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    /// <summary>Deletion error payload maps to a failed result with the agent code and no agent text.</summary>
    [Fact]
    public async Task Deletion_error_payload_maps_to_a_failed_result_with_the_agent_code_and_no_agent_text()
    {
        var logger = new RecordingLogger<AgentFtpsClient>();
        var stub = new StubFtpsService
        {
            DeleteResponse = new DeleteFtpsUserResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.NotFound,
                    Message = "no login at /var/lib/maran-ftps/alice",
                },
            },
        };

        var result = await new AgentFtpsClient(stub, logger)
            .DeleteUserAsync("alice", "files", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
        Assert.DoesNotContain("/var/lib/maran-ftps", result.Error.Code, StringComparison.Ordinal);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("/var/lib/maran-ftps/alice", logged, StringComparison.Ordinal);
    }

    /// <summary>A deletion response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_deletion_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await new AgentFtpsClient(new StubFtpsService(), NullLogger<AgentFtpsClient>.Instance)
            .DeleteUserAsync("alice", "files", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A busy account reaches the panel as an undifferentiated system failure.</summary>
    /// <remarks>
    /// Not an approval of that mapping — a record of it. The agent's per-account lock never waits,
    /// it refuses, and <c>FtpsError::AccountBusy</c> becomes <c>ERROR_CODE_SYSTEM_FAILURE</c>
    /// because the contract has no busy code. So the panel cannot tell "somebody else is changing
    /// this account right now, try again" from "your server is broken", and this test is what will
    /// go red on the day a busy code is added — which is the moment the panel must start answering
    /// differently.
    /// </remarks>
    [Fact]
    public async Task A_busy_account_reaches_the_panel_as_an_undifferentiated_system_failure()
    {
        var stub = StubFtpsService.FailingCreateWith(
            ErrorCode.SystemFailure,
            "another operation is already running for account alice");

        var result = await CreateAsync(stub, NullLogger<AgentFtpsClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    /// <summary>Calls the production creation path with fixed arguments.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <param name="logger">The logger the client writes the agent's text to.</param>
    /// <returns>What the client returned.</returns>
    private static async Task<Result<string>> CreateAsync(
        StubFtpsService stub,
        ILogger<AgentFtpsClient> logger)
    {
        var client = new AgentFtpsClient(stub, logger);

        return await client.CreateUserAsync(
            new CreateFtpsUserArguments("alice", "files", new SensitiveString(GeneratedPassword)),
            CancellationToken.None);
    }

    /// <summary>Calls the production password-change path with fixed arguments.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <param name="logger">The logger the client writes the agent's text to.</param>
    /// <returns>What the client returned.</returns>
    private static async Task<Result<bool>> SetPasswordAsync(
        StubFtpsService stub,
        ILogger<AgentFtpsClient> logger)
    {
        var client = new AgentFtpsClient(stub, logger);

        return await client.SetPasswordAsync(
            "alice",
            "files",
            new SensitiveString(ReplacementPassword),
            CancellationToken.None);
    }
}
