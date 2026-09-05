using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.SystemService;

/// <summary>Maps the agent's SystemService handshake onto <see cref="Result{T}"/>.</summary>
public sealed class AgentSystemClient : IAgentSystemClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly ISystemServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentSystemClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual <c>GetAgentInfo</c> call.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentSystemClient(ISystemServiceInvoker invoker, ILogger<AgentSystemClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentSystemClient(GrpcChannel channel, ILogger<AgentSystemClient> logger)
        : this(new GrpcSystemServiceInvoker(new Maran.Agent.V1.SystemService.SystemServiceClient(channel)), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        var response = await _invoker.GetAgentInfoAsync(ct);

        return response.ResultCase switch
        {
            GetAgentInfoResponse.ResultOneofCase.Ok => Result<AgentInfoDto>.Ok(ToDto(response.Ok)),
            GetAgentInfoResponse.ResultOneofCase.Error => Result<AgentInfoDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetInfoAsync))),
            _ => Result<AgentInfoDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>Converts the wire <see cref="AgentInfo"/> into the backend-facing DTO.</summary>
    /// <param name="info">The successful handshake payload.</param>
    private static AgentInfoDto ToDto(AgentInfo info)
    {
        return new(info.Version, info.DistroId, ToFamily(info.Family), info.ProtoVersion);
    }

    /// <summary>Renders a wire <see cref="DistroFamily"/> as the DTO's stable lowercase string.</summary>
    /// <param name="family">The distro family reported by the agent.</param>
    private static string ToFamily(DistroFamily family)
    {
        return family switch
        {
            DistroFamily.Debian => "debian",
            DistroFamily.Rhel => "rhel",
            DistroFamily.Unspecified => "unspecified",
            _ => "unspecified",
        };
    }

}
