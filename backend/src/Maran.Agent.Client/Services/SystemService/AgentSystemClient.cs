using Grpc.Core;
using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.SystemService;

/// <summary>
/// The panel's half of the agent handshake: asks <c>GetAgentInfo</c> and answers with the agent's
/// identity only when the agent speaks a contract revision inside <see cref="AgentProtoVersionWindow"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every answer is a <see cref="Result{T}"/>, and there are four kinds of failure.</b> An agent
/// that answered outside the window fails as <see cref="AgentProtoVersionWindow.IncompatibleCode"/>
/// (<see cref="ErrorType.Unavailable"/>): it answered, but in a contract whose zeros and meanings this
/// panel cannot vouch for, so nothing it said is handed on — not even the backup root. An agent that
/// refused the call in its payload fails with whatever <see cref="AgentErrorTranslator"/> makes of it.
/// A transport that never got an answer — no socket, nobody listening, a status the agent raised
/// instead of answering — fails with whatever <see cref="AgentTransportFailure"/> makes of it,
/// which for an unreachable agent is <c>AgentUnavailable</c>. A response with neither branch of its
/// oneof set is <c>AgentInvalidResponse</c>.
/// </para>
/// <para>
/// <b>A cancelled call is not an outage.</b> The panel's own channel
/// (<see cref="Channels.AgentChannel"/>) reports a cancelled token as an
/// <see cref="OperationCanceledException"/>, which passes through this client untouched: a caller that
/// gave up sees its own cancellation, and a resilience pipeline whose timeout cancelled the token sees
/// the exception its timeout strategy turns into a timeout. This client does not rely on that setting,
/// because its public constructor takes any channel, and one left at the library's default reports
/// the same cancellation as an <see cref="RpcException"/> with <see cref="StatusCode.Cancelled"/> —
/// which the mapping below would answer as an unreachable agent. So an <see cref="RpcException"/>
/// raised while the token is cancelled is rethrown as an <see cref="OperationCanceledException"/>
/// carrying that token, never mapped.
/// </para>
/// <para>
/// <b>Where this sits relative to the retry.</b> Because a transport failure is returned rather than
/// thrown, a pipeline wrapped around this client sees a failed <see cref="Result{T}"/>, not an
/// exception, for an unreachable agent: a retry meant for that case has to be decided on the result.
/// A retry of this call is always safe — the handshake reads and changes nothing on the host.
/// </para>
/// </remarks>
public sealed class AgentSystemClient : IAgentSystemClient
{
    /// <summary>
    /// Pre-compiled log delegate for a handshake answered outside the window: the one line that names
    /// the revision the agent reported, since the <see cref="Error"/> carries only a code.
    /// </summary>
    private static readonly Action<ILogger, uint, uint, uint, Exception?> LogIncompatible =
        LoggerMessage.Define<uint, uint, uint>(
            LogLevel.Warning,
            new EventId(1, nameof(AgentSystemClient)),
            "The agent reports contract revision {ReportedRevision}, outside the revisions "
                + "{MinimumRevision}..{MaximumRevision} this panel drives; the handshake is refused");

    /// <summary>
    /// Pre-compiled log delegate for a handshake the transport could not complete. Debug, because the
    /// consumers report the outcome themselves (the health report, the startup check) and an absent
    /// agent is polled on every health request; the exception is attached so its detail — the socket
    /// path, the host's own socket error — is still there for an operator who turns the level up.
    /// </summary>
    private static readonly Action<ILogger, StatusCode, Exception?> LogTransportFailure =
        LoggerMessage.Define<StatusCode>(
            LogLevel.Debug,
            new EventId(2, nameof(AgentSystemClient)),
            "The agent handshake failed in transport with {StatusCode}");

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
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct"/> was cancelled while the call was in flight — by the caller, or by a
    /// pipeline timeout that owns the token. Never reported as an unreachable agent.
    /// </exception>
    public async Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        GetAgentInfoResponse response;
        try
        {
            response = await _invoker.GetAgentInfoAsync(ct);
        }
        catch (RpcException exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("The agent handshake was cancelled.", exception, ct);
        }
        catch (RpcException exception)
        {
            LogTransportFailure(_logger, exception.StatusCode, exception);
            return Result<AgentInfoDto>.Fail(AgentTransportFailure.ToError(exception));
        }

        return response.ResultCase switch
        {
            GetAgentInfoResponse.ResultOneofCase.Ok => Admit(response.Ok),
            GetAgentInfoResponse.ResultOneofCase.Error => Result<AgentInfoDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetInfoAsync))),
            _ => Result<AgentInfoDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>
    /// Decides whether a successful handshake may be handed on: inside <see cref="AgentProtoVersionWindow"/>
    /// it becomes the agent's identity, outside it the handshake is refused and the refusal is logged with
    /// the revision the agent reported.
    /// </summary>
    /// <param name="info">The successful handshake payload.</param>
    /// <returns>
    /// The identity, or <see cref="AgentProtoVersionWindow.IncompatibleCode"/> with
    /// <see cref="ErrorType.Unavailable"/> — a 503 that says "try again once the upgrade is finished",
    /// because a panel and an agent from one release are exactly what the next attempt needs.
    /// </returns>
    private Result<AgentInfoDto> Admit(AgentInfo info)
    {
        if (!AgentProtoVersionWindow.Contains(info.ProtoVersion))
        {
            LogIncompatible(
                _logger,
                info.ProtoVersion,
                AgentProtoVersionWindow.Minimum,
                AgentProtoVersionWindow.Maximum,
                null);
            return Result<AgentInfoDto>.Fail(Error.Of(AgentProtoVersionWindow.IncompatibleCode, ErrorType.Unavailable));
        }

        return Result<AgentInfoDto>.Ok(ToDto(info));
    }

    /// <summary>Converts the wire <see cref="AgentInfo"/> into the backend-facing DTO.</summary>
    /// <param name="info">The successful handshake payload.</param>
    private static AgentInfoDto ToDto(AgentInfo info)
    {
        return new(info.Version, info.DistroId, ToFamily(info.Family), info.ProtoVersion, info.BackupRoot);
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
