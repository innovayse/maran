using System.Runtime.CompilerServices;
using Grpc.Net.Client;
using Maran.Agent.Client.Channels;
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

    /// <summary>Creates a client whose every call goes to the server the current agent target selects.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentSitesClient(IAgentChannels channels, ILogger<AgentSitesClient> logger)
        : this(new GrpcSitesServiceInvoker(channels), logger)
    {
    }

    /// <summary>Creates a client over one fixed <paramref name="channel"/>, whatever the target.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentSitesClient(GrpcChannel channel, ILogger<AgentSitesClient> logger)
        : this(new FixedAgentChannels(channel), logger)
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
        SiteWebServer webServer,
        SiteNodeApp node,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        SiteOptionsDescriptor options,
        CancellationToken cancellationToken)
    {
        var request = new CreateSiteRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            BackendType = SiteDescriptor.ToWireBackend(kind),
            PhpVersion = phpVersion,
            ProxyUpstream = proxyUpstream,
            WebServer = SiteDescriptor.ToWireWebServer(webServer),
            NodeVersion = node.Version,
            AppPort = node.Port,
            StartCommand = node.StartCommand,
            MaxChildren = maxChildren,
            Options = options.ToWire(),
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
    public async Task<Result> ChangePhpVersionAsync(
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
            UpdateSitePhpVersionResponse.ResultOneofCase.Ok => Result.Ok(),
            UpdateSitePhpVersionResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ChangePhpVersionAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> EnableAsync(
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
            EnableSiteResponse.ResultOneofCase.Ok => Result.Ok(),
            EnableSiteResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(EnableAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> DisableAsync(
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
            DisableSiteResponse.ResultOneofCase.Ok => Result.Ok(),
            DisableSiteResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DisableAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        SiteWebServer webServer,
        CancellationToken cancellationToken)
    {
        var request = new DeleteSiteRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            RetiredPhpVersion = retiredPhpVersion,
            WebServer = SiteDescriptor.ToWireWebServer(webServer),
        };
        var response = await _invoker.DeleteSiteAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DeleteSiteResponse.ResultOneofCase.Ok => Result.Ok(),
            DeleteSiteResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DeleteAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<SiteTrafficReading>> SummariseTrafficAsync(
        string accountUsername,
        string domain,
        SiteTrafficCursor? cursor,
        CancellationToken cancellationToken)
    {
        var request = new SummariseSiteTrafficRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            Cursor = SiteTrafficMapper.ToWire(cursor),
        };
        var response = await _invoker.SummariseSiteTrafficAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SummariseSiteTrafficResponse.ResultOneofCase.Ok => SiteTrafficMapper.Read(response.Ok),
            SummariseSiteTrafficResponse.ResultOneofCase.Error => Result<SiteTrafficReading>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(SummariseTrafficAsync))),
            _ => Result<SiteTrafficReading>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<PrunedSiteLogs>> PruneLogsAsync(
        string accountUsername,
        string domain,
        uint retainRotations,
        CancellationToken cancellationToken)
    {
        var request = new PruneSiteLogsRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            RetainRotations = retainRotations,
        };
        var response = await _invoker.PruneSiteLogsAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            PruneSiteLogsResponse.ResultOneofCase.Ok => Result<PrunedSiteLogs>.Ok(
                new PrunedSiteLogs(response.Ok.RemovedFiles)),
            PruneSiteLogsResponse.ResultOneofCase.Error => Result<PrunedSiteLogs>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(PruneLogsAsync))),
            _ => Result<PrunedSiteLogs>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> ReloadWebServerAsync(SiteWebServer webServer, CancellationToken cancellationToken)
    {
        var request = new ReloadWebServerRequest { WebServer = SiteDescriptor.ToWireWebServer(webServer) };
        var response = await _invoker.ReloadWebServerAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ReloadWebServerResponse.ResultOneofCase.Ok => Result.Ok(),
            ReloadWebServerResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ReloadWebServerAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A transport failure ends the tail in a <see cref="SiteLogFailed"/> event carrying the
    /// code of <see cref="AgentTransportFailure.ToError"/>, never in the raw exception.
    /// </remarks>
    public IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        CancellationToken cancellationToken)
    {
        return AgentStreamGuard.EndOnTransportFailureAsync(
            TailLogEventsAsync(accountUsername, domain, logSource, historyLines, cancellationToken),
            error =>
            {
                return new SiteLogFailed(error.Code);
            },
            cancellationToken);
    }

    /// <summary>Streams the log events the agent states, without guarding against the transport failing.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="domain">The site whose log is followed.</param>
    /// <param name="logSource">Which log to follow.</param>
    /// <param name="historyLines">How many existing lines to replay first.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>Line events followed by one terminal event.</returns>
    private async IAsyncEnumerable<SiteLogEvent> TailLogEventsAsync(
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
                yield return new SiteLogEnded(SiteLogEndKind.Cancelled);
                yield break;
            }

            if (response.ResultCase == TailSiteLogResponse.ResultOneofCase.Ok)
            {
                yield return new SiteLogLine(response.Ok.Line, response.Ok.Historical);
                continue;
            }

            if (response.ResultCase == TailSiteLogResponse.ResultOneofCase.Error)
            {
                yield return ToTerminalEvent(response.Error);
                yield break;
            }

            // A message carrying neither branch is not a line and not an ending the agent named.
            // Reporting it as a failure keeps the invariant that a tail always ends in a stated way.
            yield return new SiteLogFailed(nameof(ErrorMessages.AgentInvalidResponse));
            yield break;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            yield return new SiteLogEnded(SiteLogEndKind.Cancelled);
            yield break;
        }

        // The agent closed the stream without a terminal message: its natural end, and the only
        // ending that is not an interruption.
        yield return new SiteLogEnded(SiteLogEndKind.Completed);
    }

    /// <summary>Maps the panel's log selector onto its wire counterpart.</summary>
    /// <param name="logSource">Which log the caller asked for.</param>
    /// <returns>The wire value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for a selector this mapping does not know: a defect of the panel, never sent on as the
    /// wire's unspecified value.
    /// </exception>
    private static SiteLogKind ToWireLogKind(SiteLogSource logSource)
    {
        return logSource switch
        {
            SiteLogSource.Access => SiteLogKind.Access,
            SiteLogSource.Error => SiteLogKind.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(logSource), logSource, "Unknown site log source."),
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
            return new SiteLogEnded(SiteLogEndKind.Dropped);
        }

        if (error.Code == ErrorCode.StreamIdle)
        {
            return new SiteLogEnded(SiteLogEndKind.Idle);
        }

        return new SiteLogFailed(AgentErrorTranslator.ToError(_logger, error, nameof(TailLogAsync)).Code);
    }
}
