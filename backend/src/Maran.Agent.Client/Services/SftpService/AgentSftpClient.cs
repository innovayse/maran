using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.SftpService;

/// <summary>Maps the agent's SFTP rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// Same shape as the other agent clients: the failure branch of the response oneof becomes a typed
/// <see cref="Error"/> carrying only a code, and the agent's own diagnostic text — which can name
/// the jail's absolute path and the sshd configuration — is logged rather than returned
/// (rules/security.md item 8).
///
/// No method here takes a chroot path, because the contract has no such field: the jail is derived
/// from the account name and created root-owned by the agent. A request cannot name the directory it
/// will be confined to, which removes the escape class rather than defending against it.
///
/// The password travels in the request and appears in no log line: it is held in a
/// <see cref="SensitiveString"/> so nothing can print it by accident, and it is handed to the error
/// translator so the agent quoting it back is stripped before the text is logged.
/// </remarks>
public sealed class AgentSftpClient : IAgentSftpClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly ISftpServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentSftpClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentSftpClient(ISftpServiceInvoker invoker, ILogger<AgentSftpClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentSftpClient(GrpcChannel channel, ILogger<AgentSftpClient> logger)
        : this(new GrpcSftpServiceInvoker(new V1.SftpService.SftpServiceClient(channel)), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<string>> CreateAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        var request = new CreateSftpUserRequest
        {
            AccountUsername = accountUsername,
            SftpUsername = sftpUsername,

            // The one place the value is unwrapped, and it is unwrapped straight onto the wire.
            Password = password.Reveal(),
        };
        var response = await _invoker.CreateSftpUserAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            CreateSftpUserResponse.ResultOneofCase.Ok => Result<string>.Ok(response.Ok.SftpUsername),
            CreateSftpUserResponse.ResultOneofCase.Error => Result<string>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(CreateAsync), password)),
            _ => Result<string>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        var request = new SetSftpPasswordRequest
        {
            AccountUsername = accountUsername,
            SftpUsername = sftpUsername,
            Password = password.Reveal(),
        };
        var response = await _invoker.SetSftpPasswordAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetSftpPasswordResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            SetSftpPasswordResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(SetPasswordAsync), password)),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string sftpUsername,
        CancellationToken cancellationToken)
    {
        var request = new DeleteSftpUserRequest
        {
            AccountUsername = accountUsername,
            SftpUsername = sftpUsername,
        };
        var response = await _invoker.DeleteSftpUserAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DeleteSftpUserResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            DeleteSftpUserResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DeleteAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }
}
