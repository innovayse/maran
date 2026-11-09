using Grpc.Net.Client;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.DnsService;

/// <summary>Maps the agent's DNS rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// <para>
/// Same shape as <see cref="FtpsService.AgentFtpsClient"/>, deliberately: the failure branch of the
/// response oneof becomes a typed <see cref="Error"/> carrying only a code, and the agent's own diagnostic
/// text — which can name the zone directory and the checker's output — is logged rather than returned
/// (rules/security.md item 8). A response with neither branch set is refused rather than read as success.
/// </para>
/// <para>
/// Every wire error goes through <see cref="AgentErrorTranslator"/> and there is no second path around it.
/// Nothing here carries a secret: a zone's records are public data the name server publishes.
/// </para>
/// </remarks>
public sealed class AgentDnsClient : IAgentDnsClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IDnsServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentDnsClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructors).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentDnsClient(IDnsServiceInvoker invoker, ILogger<AgentDnsClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client whose every call goes to the server the current agent target selects.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentDnsClient(IAgentChannels channels, ILogger<AgentDnsClient> logger)
        : this(new GrpcDnsServiceInvoker(channels), logger)
    {
    }

    /// <summary>Creates a client over one fixed <paramref name="channel"/>, whatever the target.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentDnsClient(GrpcChannel channel, ILogger<AgentDnsClient> logger)
        : this(new FixedAgentChannels(channel), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<DnsDaemonStatusDto>> EnableAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.EnableDnsAsync(new EnableDnsRequest(), cancellationToken);

        return response.ResultCase switch
        {
            EnableDnsResponse.ResultOneofCase.Ok => Result<DnsDaemonStatusDto>.Ok(DnsZoneMapper.ToStatus(response.Ok.Status)),
            EnableDnsResponse.ResultOneofCase.Error => Result<DnsDaemonStatusDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(EnableAsync))),
            _ => Result<DnsDaemonStatusDto>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<DnsDaemonStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.DisableDnsAsync(new DisableDnsRequest(), cancellationToken);

        return response.ResultCase switch
        {
            DisableDnsResponse.ResultOneofCase.Ok => Result<DnsDaemonStatusDto>.Ok(DnsZoneMapper.ToStatus(response.Ok.Status)),
            DisableDnsResponse.ResultOneofCase.Error => Result<DnsDaemonStatusDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DisableAsync))),
            _ => Result<DnsDaemonStatusDto>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<DnsDaemonStatusDto>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.GetDnsStatusAsync(new GetDnsStatusRequest(), cancellationToken);

        return response.ResultCase switch
        {
            GetDnsStatusResponse.ResultOneofCase.Ok => Result<DnsDaemonStatusDto>.Ok(DnsZoneMapper.ToStatus(response.Ok.Status)),
            GetDnsStatusResponse.ResultOneofCase.Error => Result<DnsDaemonStatusDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetStatusAsync))),
            _ => Result<DnsDaemonStatusDto>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<DnsZoneServedDto>> PutZoneAsync(
        DnsZoneDefinition zone,
        CancellationToken cancellationToken)
    {
        var response = await _invoker.PutZoneAsync(DnsZoneMapper.ToRequest(zone), cancellationToken);

        return response.ResultCase switch
        {
            PutZoneResponse.ResultOneofCase.Ok => Result<DnsZoneServedDto>.Ok(
                new DnsZoneServedDto(zone.Name, response.Ok.Serial)),
            PutZoneResponse.ResultOneofCase.Error => Result<DnsZoneServedDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(PutZoneAsync))),
            _ => Result<DnsZoneServedDto>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        var response = await _invoker.DeleteZoneAsync(new DeleteZoneRequest { ZoneName = zoneName }, cancellationToken);

        return response.ResultCase switch
        {
            DeleteZoneResponse.ResultOneofCase.Ok => Result.Ok(),
            DeleteZoneResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DeleteZoneAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<DnsZoneServedDto>> GetZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        var response = await _invoker.GetZoneAsync(new GetZoneRequest { ZoneName = zoneName }, cancellationToken);

        return response.ResultCase switch
        {
            GetZoneResponse.ResultOneofCase.Ok when response.Ok.Served => Result<DnsZoneServedDto>.Ok(
                new DnsZoneServedDto(zoneName, response.Ok.Serial)),
            GetZoneResponse.ResultOneofCase.Ok => Result<DnsZoneServedDto>.Fail(
                Error.Of(nameof(ErrorMessages.AgentNotFound), ErrorType.NotFound)),
            GetZoneResponse.ResultOneofCase.Error => Result<DnsZoneServedDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetZoneAsync))),
            _ => Result<DnsZoneServedDto>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<string>>> ListZonesAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.ListZonesAsync(new ListZonesRequest(), cancellationToken);

        if (response.ResultCase == ListZonesResponse.ResultOneofCase.Error)
        {
            return Result<IReadOnlyList<string>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListZonesAsync)));
        }

        if (response.ResultCase != ListZonesResponse.ResultOneofCase.Ok)
        {
            return Result<IReadOnlyList<string>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
        }

        IReadOnlyList<string> served = response.Ok.ZoneNames.ToList();

        return Result<IReadOnlyList<string>>.Ok(served);
    }
}
