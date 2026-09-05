using System.Runtime.CompilerServices;
using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.SitesService;

/// <summary>Maps the agent's site rpcs onto <see cref="Result{T}"/> and typed stream events.</summary>
/// <remarks>
/// The agent answers failures inside the response's oneof rather than as a gRPC status, because
/// "this site already exists" is an answer the panel acts on. This client turns that branch into a
/// typed <see cref="Error"/> whose code the module maps to an HTTP status, and logs the agent's own
/// diagnostic text — which is operator-facing and must not reach a customer (rules/security.md
/// item 8). That text can name absolute paths on the host and can carry a failing <c>nginx -t</c>
/// excerpt; neither ever travels outward from here.
/// </remarks>
public sealed class AgentSitesClient : IAgentSitesClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly ISitesServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentSitesClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentSitesClient(ISitesServiceInvoker invoker, ILogger<AgentSitesClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentSitesClient(GrpcChannel channel, ILogger<AgentSitesClient> logger)
        : this(new GrpcSitesServiceInvoker(new V1.SitesService.SitesServiceClient(channel)), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<CreatedSiteDto>> CreateAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<string> aliases,
        SiteBackendKind kind,
        string phpVersion,
        string proxyUpstream,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken)
    {
        var request = new CreateSiteRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            BackendType = SiteDescriptor.ToWireBackend(kind),
            PhpVersion = phpVersion,
            ProxyUpstream = proxyUpstream,
            MaxChildren = maxChildren,
        };
        request.Aliases.AddRange(aliases);
        foreach (var setting in settingOverrides)
        {
            request.Overrides.Add(new PhpSetting { Name = setting.Name, Value = setting.Value });
        }

        var response = await _invoker.CreateSiteAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            CreateSiteResponse.ResultOneofCase.Ok => Result<CreatedSiteDto>.Ok(
                new CreatedSiteDto(response.Ok.DocumentRoot)),
            CreateSiteResponse.ResultOneofCase.Error => Result<CreatedSiteDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(CreateAsync))),
            _ => Result<CreatedSiteDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> ChangePhpVersionAsync(
        string accountUsername,
        string domain,
        string phpVersion,
        SiteDescriptor site,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        bool removePreviousPool,
        CancellationToken cancellationToken)
    {
        var request = new UpdateSitePhpVersionRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            PhpVersion = phpVersion,
            Site = site.ToWire(),
            MaxChildren = maxChildren,
            RemovePreviousPool = removePreviousPool,
        };
        foreach (var setting in settingOverrides)
        {
            request.Overrides.Add(new PhpSetting { Name = setting.Name, Value = setting.Value });
        }

        var response = await _invoker.UpdateSitePhpVersionAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            UpdateSitePhpVersionResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            UpdateSitePhpVersionResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ChangePhpVersionAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> EnableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        var request = new EnableSiteRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            Site = site.ToWire(),
        };
        var response = await _invoker.EnableSiteAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            EnableSiteResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            EnableSiteResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(EnableAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DisableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        var request = new DisableSiteRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            Site = site.ToWire(),
        };
        var response = await _invoker.DisableSiteAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DisableSiteResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            DisableSiteResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DisableAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        CancellationToken cancellationToken)
    {
        var request = new DeleteSiteRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            RetiredPhpVersion = retiredPhpVersion,
        };
        var response = await _invoker.DeleteSiteAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DeleteSiteResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            DeleteSiteResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DeleteAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> ReloadWebServerAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.ReloadWebServerAsync(new ReloadWebServerRequest(), cancellationToken);

        return response.ResultCase switch
        {
            ReloadWebServerResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            ReloadWebServerResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ReloadWebServerAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new TailSiteLogRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            Kind = ToWireLogKind(logSource),
            HistoryLines = historyLines,
        };

        await foreach (var response in _invoker.TailSiteLogAsync(request, cancellationToken))
        {
            // Checked here rather than left to the transport. A cancelled tail that fell out of the
            // loop would end in Completed — "the log had nothing more to say" — which is the exact
            // silent truncation this event type exists to prevent, and it would also keep pulling
            // messages the caller has stopped wanting.
            if (cancellationToken.IsCancellationRequested)
            {
                yield return new SiteLogEvent(SiteLogEventKind.Cancelled, string.Empty, false, null);
                yield break;
            }

            if (response.ResultCase == TailSiteLogResponse.ResultOneofCase.Ok)
            {
                yield return new SiteLogEvent(
                    SiteLogEventKind.Line,
                    response.Ok.Line,
                    response.Ok.Historical,
                    null);
                continue;
            }

            if (response.ResultCase == TailSiteLogResponse.ResultOneofCase.Error)
            {
                yield return ToTerminalEvent(response.Error);
                yield break;
            }

            // A message carrying neither branch is not a line and not an ending the agent named.
            // Reporting it as a failure keeps the invariant that a tail always ends in a stated way.
            yield return new SiteLogEvent(
                SiteLogEventKind.Failed,
                string.Empty,
                false,
                nameof(ErrorMessages.AgentInvalidResponse));
            yield break;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            yield return new SiteLogEvent(SiteLogEventKind.Cancelled, string.Empty, false, null);
            yield break;
        }

        // The agent closed the stream without a terminal message: its natural end, and the only
        // ending that is not an interruption.
        yield return new SiteLogEvent(SiteLogEventKind.Completed, string.Empty, false, null);
    }

    /// <summary>Maps the panel's log selector onto its wire counterpart.</summary>
    /// <param name="logSource">Which log the caller asked for.</param>
    /// <returns>The wire value; an unknown selector becomes the unspecified value the agent refuses.</returns>
    private static SiteLogKind ToWireLogKind(SiteLogSource logSource)
    {
        return logSource switch
        {
            SiteLogSource.Access => SiteLogKind.Access,
            SiteLogSource.Error => SiteLogKind.Error,
            _ => SiteLogKind.Unspecified,
        };
    }

    /// <summary>Turns the terminal error of a tail stream into the event that ends the sequence.</summary>
    /// <param name="error">The failure payload that closed the stream.</param>
    /// <returns>
    /// The dropped or idle ending where the agent named one, and a failure carrying the typed code
    /// otherwise. The three are kept apart because a caller must be able to say "nothing more was
    /// logged" without calling it a fault, and must not treat a dropped stream as the log's end.
    /// </returns>
    private SiteLogEvent ToTerminalEvent(AgentError error)
    {
        if (error.Code == ErrorCode.StreamDropped)
        {
            return new SiteLogEvent(SiteLogEventKind.Dropped, string.Empty, false, null);
        }

        if (error.Code == ErrorCode.StreamIdle)
        {
            return new SiteLogEvent(SiteLogEventKind.Idle, string.Empty, false, null);
        }

        return new SiteLogEvent(
            SiteLogEventKind.Failed,
            string.Empty,
            false,
            AgentErrorTranslator.ToError(_logger, error, nameof(TailLogAsync)).Code);
    }
}
