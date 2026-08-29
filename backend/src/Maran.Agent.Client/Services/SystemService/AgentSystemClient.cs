using Grpc.Net.Client;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.SystemService;

/// <summary>Maps the agent's SystemService handshake onto <see cref="Result{T}"/>.</summary>
public sealed class AgentSystemClient : IAgentSystemClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly ISystemServiceInvoker _invoker;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual <c>GetAgentInfo</c> call.</param>
    internal AgentSystemClient(ISystemServiceInvoker invoker)
    {
        _invoker = invoker;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    public AgentSystemClient(GrpcChannel channel)
        : this(new GrpcSystemServiceInvoker(new Maran.Agent.V1.SystemService.SystemServiceClient(channel)))
    {
    }

    /// <inheritdoc/>
    public async Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        var response = await _invoker.GetAgentInfoAsync(ct);

        return response.ResultCase switch
        {
            GetAgentInfoResponse.ResultOneofCase.Ok => Result<AgentInfoDto>.Ok(ToDto(response.Ok)),
            GetAgentInfoResponse.ResultOneofCase.Error => Result<AgentInfoDto>.Fail(ToError(response.Error)),
            _ => Result<AgentInfoDto>.Fail(
                Error.Of("agent.invalid_response", "Agent returned neither a result nor an error.")),
        };
    }

    /// <summary>Converts the wire <see cref="AgentInfo"/> into the backend-facing DTO.</summary>
    /// <param name="info">The successful handshake payload.</param>
    private static AgentInfoDto ToDto(AgentInfo info) =>
        new(info.Version, info.DistroId, ToFamily(info.Family), info.ProtoVersion);

    /// <summary>Renders a wire <see cref="DistroFamily"/> as the DTO's stable lowercase string.</summary>
    /// <param name="family">The distro family reported by the agent.</param>
    private static string ToFamily(DistroFamily family) => family switch
    {
        DistroFamily.Debian => "debian",
        DistroFamily.Rhel => "rhel",
        DistroFamily.Unspecified => "unspecified",
        _ => "unspecified",
    };

    /// <summary>Converts a wire <see cref="AgentError"/> into a <see cref="SharedKernel.Results.Error"/>.</summary>
    /// <param name="error">The failure payload returned by the agent.</param>
    private static Error ToError(AgentError error) => Error.Of(ToErrorCode(error.Code), error.Message);

    /// <summary>Maps a wire <see cref="ErrorCode"/> to its stable "agent.*" error code string.</summary>
    /// <param name="code">The failure category reported by the agent.</param>
    private static string ToErrorCode(ErrorCode code) => code switch
    {
        ErrorCode.Unspecified => "agent.unspecified",
        ErrorCode.InvalidInput => "agent.invalid_input",
        ErrorCode.AlreadyExists => "agent.already_exists",
        ErrorCode.NotFound => "agent.not_found",
        ErrorCode.ValidationFailed => "agent.validation_failed",
        ErrorCode.SystemFailure => "agent.system_failure",
        _ => "agent.unspecified",
    };
}
