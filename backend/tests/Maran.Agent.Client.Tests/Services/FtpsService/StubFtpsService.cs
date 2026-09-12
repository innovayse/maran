using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.FtpsService;

/// <summary>Stub of <c>IFtpsServiceInvoker</c> returning canned responses and keeping every request.</summary>
/// <remarks>
/// Every captured request here is read by a test, for the reason the database stub gives: a recorded
/// request nothing asserts makes the mapping look covered while every field of it is free to change.
/// </remarks>
internal sealed class StubFtpsService : IFtpsServiceInvoker
{
    /// <summary>Response returned from <see cref="EnableFtpsAsync"/>.</summary>
    public EnableFtpsResponse EnableResponse { get; set; } = new();

    /// <summary>The last enable request the stub received, for asserting the mapping.</summary>
    public EnableFtpsRequest? LastEnableRequest { get; private set; }

    /// <summary>Response returned from <see cref="DisableFtpsAsync"/>.</summary>
    public DisableFtpsResponse DisableResponse { get; set; } = new();

    /// <summary>The last disable request the stub received; asserted to be the empty message.</summary>
    public DisableFtpsRequest? LastDisableRequest { get; private set; }

    /// <summary>Response returned from <see cref="GetFtpsStatusAsync"/>.</summary>
    public GetFtpsStatusResponse StatusResponse { get; set; } = new();

    /// <summary>The last status request the stub received, for asserting the hostname reaches the agent.</summary>
    public GetFtpsStatusRequest? LastStatusRequest { get; private set; }

    /// <summary>Response returned from <see cref="ReloadFtpsTlsAsync"/>.</summary>
    public ReloadFtpsTlsResponse ReloadResponse { get; set; } = new();

    /// <summary>The last TLS-reload request the stub received; asserted to be the empty message.</summary>
    public ReloadFtpsTlsRequest? LastReloadRequest { get; private set; }

    /// <summary>Response returned from <see cref="CreateFtpsUserAsync"/>.</summary>
    public CreateFtpsUserResponse CreateResponse { get; set; } = new();

    /// <summary>The last creation request the stub received, for asserting the mapping.</summary>
    public CreateFtpsUserRequest? LastCreateRequest { get; private set; }

    /// <summary>Response returned from <see cref="SetFtpsPasswordAsync"/>.</summary>
    public SetFtpsPasswordResponse SetPasswordResponse { get; set; } = new();

    /// <summary>The last password-change request the stub received, for asserting the mapping.</summary>
    public SetFtpsPasswordRequest? LastSetPasswordRequest { get; private set; }

    /// <summary>Response returned from <see cref="DeleteFtpsUserAsync"/>.</summary>
    public DeleteFtpsUserResponse DeleteResponse { get; set; } = new();

    /// <summary>The last deletion request the stub received, for asserting the mapping.</summary>
    public DeleteFtpsUserRequest? LastDeleteRequest { get; private set; }

    /// <summary>Builds a stub whose creation call fails with the agent's own words.</summary>
    /// <param name="code">The failure category the agent reports.</param>
    /// <param name="message">The agent's operator-facing sentence.</param>
    /// <returns>The configured stub.</returns>
    public static StubFtpsService FailingCreateWith(ErrorCode code, string message)
    {
        return new StubFtpsService
        {
            CreateResponse = new CreateFtpsUserResponse
            {
                Error = new AgentError { Code = code, Message = message },
            },
        };
    }

    /// <summary>Builds a stub whose password change fails with the agent's own tool output.</summary>
    /// <param name="code">The failure category the agent reports.</param>
    /// <param name="toolOutput">What the underlying tool printed.</param>
    /// <returns>The configured stub.</returns>
    public static StubFtpsService FailingSetPasswordWith(ErrorCode code, string toolOutput)
    {
        return new StubFtpsService
        {
            SetPasswordResponse = new SetFtpsPasswordResponse
            {
                Error = new AgentError { Code = code, ToolOutput = toolOutput },
            },
        };
    }

    /// <inheritdoc/>
    public Task<EnableFtpsResponse> EnableFtpsAsync(
        EnableFtpsRequest request,
        CancellationToken cancellationToken)
    {
        LastEnableRequest = request;
        return Task.FromResult(EnableResponse);
    }

    /// <inheritdoc/>
    public Task<DisableFtpsResponse> DisableFtpsAsync(
        DisableFtpsRequest request,
        CancellationToken cancellationToken)
    {
        LastDisableRequest = request;
        return Task.FromResult(DisableResponse);
    }

    /// <inheritdoc/>
    public Task<GetFtpsStatusResponse> GetFtpsStatusAsync(
        GetFtpsStatusRequest request,
        CancellationToken cancellationToken)
    {
        LastStatusRequest = request;
        return Task.FromResult(StatusResponse);
    }

    /// <inheritdoc/>
    public Task<ReloadFtpsTlsResponse> ReloadFtpsTlsAsync(
        ReloadFtpsTlsRequest request,
        CancellationToken cancellationToken)
    {
        LastReloadRequest = request;
        return Task.FromResult(ReloadResponse);
    }

    /// <inheritdoc/>
    public Task<CreateFtpsUserResponse> CreateFtpsUserAsync(
        CreateFtpsUserRequest request,
        CancellationToken cancellationToken)
    {
        LastCreateRequest = request;
        return Task.FromResult(CreateResponse);
    }

    /// <inheritdoc/>
    public Task<SetFtpsPasswordResponse> SetFtpsPasswordAsync(
        SetFtpsPasswordRequest request,
        CancellationToken cancellationToken)
    {
        LastSetPasswordRequest = request;
        return Task.FromResult(SetPasswordResponse);
    }

    /// <inheritdoc/>
    public Task<DeleteFtpsUserResponse> DeleteFtpsUserAsync(
        DeleteFtpsUserRequest request,
        CancellationToken cancellationToken)
    {
        LastDeleteRequest = request;
        return Task.FromResult(DeleteResponse);
    }
}
