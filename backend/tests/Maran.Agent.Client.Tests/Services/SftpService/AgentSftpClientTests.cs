using Maran.Agent.Client.Services.SftpService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.SftpService;

/// <summary>Mapping contract of AgentSftpClient (proto oneof to Result, and every request field).</summary>
public sealed class AgentSftpClientTests
{
    /// <summary>The password a creation call carries, long enough to be a real generated one.</summary>
    private const string GeneratedPassword = "Qm4-brisk-otter-91";

    /// <summary>The password a change call carries, distinct from the creation one.</summary>
    private const string ReplacementPassword = "Vd2-amber-heron-77";

    /// <summary>Creation ok payload maps to the fully qualified login the agent created.</summary>
    [Fact]
    public async Task Creation_ok_payload_maps_to_the_fully_qualified_login_the_agent_created()
    {
        var stub = new StubSftpService
        {
            CreateResponse = new CreateSftpUserResponse
            {
                Ok = new CreateSftpUserOk { SftpUsername = "acc1_web" },
            },
        };

        var result = await CreateAsync(stub, NullLogger<AgentSftpClient>.Instance);

        Assert.True(result.IsSuccess);
        Assert.Equal("acc1_web", result.Value);
    }

    /// <summary>Creation sends the account the login suffix and the password.</summary>
    [Fact]
    public async Task Creation_sends_the_account_the_login_suffix_and_the_password()
    {
        var stub = new StubSftpService();

        await CreateAsync(stub, NullLogger<AgentSftpClient>.Instance);

        var request = Assert.IsType<CreateSftpUserRequest>(stub.LastCreateRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("web", request.SftpUsername);
        Assert.Equal(GeneratedPassword, request.Password);
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
        var stub = StubSftpService.FailingCreateWith(ErrorCode.AlreadyExists, "login exists");

        var result = await CreateAsync(stub, NullLogger<AgentSftpClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentAlreadyExists", result.Error!.Code);
    }

    /// <summary>A creation response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_creation_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await CreateAsync(new StubSftpService(), NullLogger<AgentSftpClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The generated password is passed to the agent and never written to the log.</summary>
    [Fact]
    public async Task The_generated_password_is_passed_to_the_agent_and_never_written_to_the_log()
    {
        var logger = new RecordingLogger<AgentSftpClient>();
        var stub = StubSftpService.FailingCreateWith(
            ErrorCode.SystemFailure,
            $"chpasswd: line 1: '{GeneratedPassword}' rejected by pam_pwquality");

        var result = await CreateAsync(stub, logger);

        Assert.Equal(GeneratedPassword, stub.LastCreateRequest!.Password);
        Assert.False(result.IsSuccess);
        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain(GeneratedPassword, logged, StringComparison.Ordinal);
        Assert.Contains("rejected by pam_pwquality", logged, StringComparison.Ordinal);
    }

    /// <summary>The password change sends the account the login suffix and the new password.</summary>
    [Fact]
    public async Task The_password_change_sends_the_account_the_login_suffix_and_the_new_password()
    {
        var stub = new StubSftpService
        {
            SetPasswordResponse = new SetSftpPasswordResponse { Ok = new SetSftpPasswordOk() },
        };

        await SetPasswordAsync(stub, NullLogger<AgentSftpClient>.Instance);

        var request = Assert.IsType<SetSftpPasswordRequest>(stub.LastSetPasswordRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("web", request.SftpUsername);
        Assert.Equal(ReplacementPassword, request.Password);
    }

    /// <summary>The password change ok payload maps to success.</summary>
    [Fact]
    public async Task The_password_change_ok_payload_maps_to_success()
    {
        var stub = new StubSftpService
        {
            SetPasswordResponse = new SetSftpPasswordResponse { Ok = new SetSftpPasswordOk() },
        };

        var result = await SetPasswordAsync(stub, NullLogger<AgentSftpClient>.Instance);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    /// <summary>The password change error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_password_change_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = StubSftpService.FailingSetPasswordWith(ErrorCode.NotFound, "chpasswd: user does not exist");

        var result = await SetPasswordAsync(stub, NullLogger<AgentSftpClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>The replacement password is stripped from the agents tool output before it is logged.</summary>
    /// <remarks>
    /// The change rpc carries a secret exactly as creation does, so it must hand the same secret to
    /// the redaction. Asserted on tool output rather than on the message because that is where a
    /// password-setting tool actually echoes its input.
    /// </remarks>
    [Fact]
    public async Task The_replacement_password_is_stripped_from_the_agents_tool_output_before_it_is_logged()
    {
        var logger = new RecordingLogger<AgentSftpClient>();
        var stub = StubSftpService.FailingSetPasswordWith(
            ErrorCode.SystemFailure,
            $"chpasswd: failed to set '{ReplacementPassword}' for alice_web");

        var result = await SetPasswordAsync(stub, logger);

        Assert.False(result.IsSuccess);
        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain(ReplacementPassword, logged, StringComparison.Ordinal);
        Assert.Contains("for alice_web", logged, StringComparison.Ordinal);
    }

    /// <summary>A password change response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_password_change_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await SetPasswordAsync(new StubSftpService(), NullLogger<AgentSftpClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Deletion sends the account and the login suffix and nothing else.</summary>
    [Fact]
    public async Task Deletion_sends_the_account_and_the_login_suffix_and_nothing_else()
    {
        var stub = new StubSftpService
        {
            DeleteResponse = new DeleteSftpUserResponse { Ok = new DeleteSftpUserOk() },
        };

        await new AgentSftpClient(stub, NullLogger<AgentSftpClient>.Instance)
            .DeleteAsync("alice", "web", CancellationToken.None);

        var request = Assert.IsType<DeleteSftpUserRequest>(stub.LastDeleteRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("web", request.SftpUsername);
    }

    /// <summary>Deletion ok payload maps to success.</summary>
    [Fact]
    public async Task Deletion_ok_payload_maps_to_success()
    {
        var stub = new StubSftpService
        {
            DeleteResponse = new DeleteSftpUserResponse { Ok = new DeleteSftpUserOk() },
        };

        var result = await new AgentSftpClient(stub, NullLogger<AgentSftpClient>.Instance)
            .DeleteAsync("alice", "web", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    /// <summary>Deletion error payload maps to a failed result with the agent code and no agent text.</summary>
    [Fact]
    public async Task Deletion_error_payload_maps_to_a_failed_result_with_the_agent_code_and_no_agent_text()
    {
        var logger = new RecordingLogger<AgentSftpClient>();
        var stub = new StubSftpService
        {
            DeleteResponse = new DeleteSftpUserResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.NotFound,
                    Message = "no login at /var/lib/maran/sftp/alice",
                },
            },
        };

        var result = await new AgentSftpClient(stub, logger)
            .DeleteAsync("alice", "web", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
        Assert.DoesNotContain("/var/lib/maran", result.Error.Code, StringComparison.Ordinal);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("/var/lib/maran/sftp/alice", logged, StringComparison.Ordinal);
    }

    /// <summary>A deletion response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_deletion_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await new AgentSftpClient(new StubSftpService(), NullLogger<AgentSftpClient>.Instance)
            .DeleteAsync("alice", "web", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Calls the production creation path with fixed arguments.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <param name="logger">The logger the client writes the agent's text to.</param>
    /// <returns>What the client returned.</returns>
    private static async Task<SharedKernel.Results.Result<string>> CreateAsync(
        StubSftpService stub,
        Microsoft.Extensions.Logging.ILogger<AgentSftpClient> logger)
    {
        var client = new AgentSftpClient(stub, logger);

        return await client.CreateAsync(
            "alice",
            "web",
            new SensitiveString(GeneratedPassword),
            CancellationToken.None);
    }

    /// <summary>Calls the production password-change path with fixed arguments.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <param name="logger">The logger the client writes the agent's text to.</param>
    /// <returns>What the client returned.</returns>
    private static async Task<SharedKernel.Results.Result<bool>> SetPasswordAsync(
        StubSftpService stub,
        Microsoft.Extensions.Logging.ILogger<AgentSftpClient> logger)
    {
        var client = new AgentSftpClient(stub, logger);

        return await client.SetPasswordAsync(
            "alice",
            "web",
            new SensitiveString(ReplacementPassword),
            CancellationToken.None);
    }
}
