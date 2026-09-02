using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.SftpService;

/// <summary>Stub of <c>ISftpServiceInvoker</c> returning canned responses and keeping every request.</summary>
/// <remarks>
/// Every captured request here is read by a test, for the reason the database stub gives: a recorded
/// request nothing asserts makes the mapping look covered while every field of it is free to change.
/// </remarks>
internal sealed class StubSftpService : ISftpServiceInvoker
{
    /// <summary>Response returned from <see cref="CreateSftpUserAsync"/>.</summary>
    public CreateSftpUserResponse CreateResponse { get; set; } = new();

    /// <summary>The last creation request the stub received, for asserting the mapping.</summary>
    public CreateSftpUserRequest? LastCreateRequest { get; private set; }

    /// <summary>Response returned from <see cref="SetSftpPasswordAsync"/>.</summary>
    public SetSftpPasswordResponse SetPasswordResponse { get; set; } = new();

    /// <summary>The last password-change request the stub received, for asserting the mapping.</summary>
    public SetSftpPasswordRequest? LastSetPasswordRequest { get; private set; }

    /// <summary>Response returned from <see cref="DeleteSftpUserAsync"/>.</summary>
    public DeleteSftpUserResponse DeleteResponse { get; set; } = new();

    /// <summary>The last deletion request the stub received, for asserting the mapping.</summary>
    public DeleteSftpUserRequest? LastDeleteRequest { get; private set; }

    /// <summary>Builds a stub whose creation call fails with the agent's own words.</summary>
    /// <param name="code">The failure category the agent reports.</param>
    /// <param name="message">The agent's operator-facing sentence.</param>
    /// <returns>The configured stub.</returns>
    public static StubSftpService FailingCreateWith(ErrorCode code, string message)
    {
        return new StubSftpService
        {
            CreateResponse = new CreateSftpUserResponse
            {
                Error = new AgentError { Code = code, Message = message },
            },
        };
    }

    /// <summary>Builds a stub whose password change fails with the agent's own tool output.</summary>
    /// <param name="code">The failure category the agent reports.</param>
    /// <param name="toolOutput">What the underlying tool printed.</param>
    /// <returns>The configured stub.</returns>
    public static StubSftpService FailingSetPasswordWith(ErrorCode code, string toolOutput)
    {
        return new StubSftpService
        {
            SetPasswordResponse = new SetSftpPasswordResponse
            {
                Error = new AgentError { Code = code, ToolOutput = toolOutput },
            },
        };
    }

    /// <inheritdoc/>
    public Task<CreateSftpUserResponse> CreateSftpUserAsync(
        CreateSftpUserRequest request,
        CancellationToken cancellationToken)
    {
        LastCreateRequest = request;
        return Task.FromResult(CreateResponse);
    }

    /// <inheritdoc/>
    public Task<SetSftpPasswordResponse> SetSftpPasswordAsync(
        SetSftpPasswordRequest request,
        CancellationToken cancellationToken)
    {
        LastSetPasswordRequest = request;
        return Task.FromResult(SetPasswordResponse);
    }

    /// <inheritdoc/>
    public Task<DeleteSftpUserResponse> DeleteSftpUserAsync(
        DeleteSftpUserRequest request,
        CancellationToken cancellationToken)
    {
        LastDeleteRequest = request;
        return Task.FromResult(DeleteResponse);
    }
}
