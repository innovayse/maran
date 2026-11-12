using System.Runtime.CompilerServices;
using Grpc.Net.Client;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.AppsService;

/// <summary>Maps the agent's app-store rpcs onto <see cref="Result{T}"/> and typed stream events.</summary>
/// <remarks>
/// Same shape as <c>AgentPhpClient</c>: the unary calls answer a <see cref="Result{T}"/>, and the two
/// server-streaming calls always end in a stated way, the agent's own success or failure, one of the two
/// stream endings it reports, or, when the transport simply stopped, a truncation, which is not a success.
/// </remarks>
public sealed class AgentAppsClient : IAgentAppsClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IAppsServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentAppsClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentAppsClient(IAppsServiceInvoker invoker, ILogger<AgentAppsClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client whose every call goes to the server the current agent target selects.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentAppsClient(IAgentChannels channels, ILogger<AgentAppsClient> logger)
        : this(new GrpcAppsServiceInvoker(channels), logger)
    {
    }

    /// <summary>Creates a client over one fixed <paramref name="channel"/>, whatever the target.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentAppsClient(GrpcChannel channel, ILogger<AgentAppsClient> logger)
        : this(new FixedAgentChannels(channel), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentApp>>> ListAppsAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.ListAppsAsync(new ListAppsRequest(), cancellationToken);

        return response.ResultCase switch
        {
            ListAppsResponse.ResultOneofCase.Ok => Result<IReadOnlyList<AgentApp>>.Ok(AppsMapper.ToApps(response.Ok)),
            ListAppsResponse.ResultOneofCase.Error => Result<IReadOnlyList<AgentApp>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListAppsAsync))),
            _ => Result<IReadOnlyList<AgentApp>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentInstallableApps>> ListInstallableAppsAsync(
        CancellationToken cancellationToken)
    {
        var response = await _invoker.ListInstallableAppsAsync(new ListInstallableAppsRequest(), cancellationToken);

        return response.ResultCase switch
        {
            ListInstallableAppsResponse.ResultOneofCase.Ok => Result<AgentInstallableApps>.Ok(
                AppsMapper.ToInstallable(response.Ok)),
            ListInstallableAppsResponse.ResultOneofCase.Error => Result<AgentInstallableApps>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListInstallableAppsAsync))),
            _ => Result<AgentInstallableApps>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A transport failure ends the stream in a <see cref="AppOperationEventKind.Failed"/> event carrying
    /// the error of <see cref="AgentTransportFailure.ToError"/>, kind included, never in the raw exception.
    /// </remarks>
    public IAsyncEnumerable<AppOperationEvent> InstallAppAsync(string appId, CancellationToken cancellationToken)
    {
        return AgentStreamGuard.EndOnTransportFailureAsync(
            EventsAsync(InstallMessagesAsync(appId, cancellationToken), nameof(InstallAppAsync), cancellationToken),
            TransportFailed,
            cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A transport failure ends the stream in a <see cref="AppOperationEventKind.Failed"/> event carrying
    /// the error of <see cref="AgentTransportFailure.ToError"/>, kind included, never in the raw exception.
    /// </remarks>
    public IAsyncEnumerable<AppOperationEvent> UninstallAppAsync(string appId, CancellationToken cancellationToken)
    {
        return AgentStreamGuard.EndOnTransportFailureAsync(
            EventsAsync(UninstallMessagesAsync(appId, cancellationToken), nameof(UninstallAppAsync), cancellationToken),
            TransportFailed,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentAppServiceStatus>> ControlServiceAsync(
        string appId,
        AgentAppServiceAction action,
        CancellationToken cancellationToken)
    {
        var response = await _invoker.ControlAppServiceAsync(
            new ControlAppServiceRequest { AppId = appId, Action = AppsMapper.ToWireAction(action) },
            cancellationToken);

        return response.ResultCase switch
        {
            ControlAppServiceResponse.ResultOneofCase.Ok => Result<AgentAppServiceStatus>.Ok(
                AppsMapper.ToServiceStatus(response.Ok.ServiceStatus)),
            ControlAppServiceResponse.ResultOneofCase.Error => Result<AgentAppServiceStatus>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ControlServiceAsync))),
            _ => Result<AgentAppServiceStatus>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>The terminal event of a stream whose transport failed.</summary>
    /// <param name="error">The typed failure of the transport.</param>
    /// <returns>The failed event carrying it.</returns>
    private static AppOperationEvent TransportFailed(Error error)
    {
        return TerminalEvent(AppOperationEventKind.Failed, error);
    }

    /// <summary>Builds a terminal event that carries no progress.</summary>
    /// <param name="kind">How the stream ended.</param>
    /// <param name="error">The typed failure for a failed ending; null for every other.</param>
    /// <returns>The terminal event.</returns>
    private static AppOperationEvent TerminalEvent(AppOperationEventKind kind, Error? error)
    {
        return new AppOperationEvent(kind, 0, string.Empty, error);
    }

    /// <summary>Folds the install stream's wire messages into the shared message shape.</summary>
    /// <param name="appId">The app to install.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>One message per wire message, in order.</returns>
    private async IAsyncEnumerable<AppStreamMessage> InstallMessagesAsync(
        string appId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new InstallAppRequest { AppId = appId };

        await foreach (var response in _invoker.InstallAppAsync(request, cancellationToken))
        {
            yield return response.ResultCase switch
            {
                InstallAppResponse.ResultOneofCase.Progress => new AppStreamMessage(
                    AppStreamBranch.Progress, response.Progress, null),
                InstallAppResponse.ResultOneofCase.Ok => new AppStreamMessage(AppStreamBranch.Completed, null, null),
                InstallAppResponse.ResultOneofCase.Error => new AppStreamMessage(
                    AppStreamBranch.Error, null, response.Error),
                _ => new AppStreamMessage(AppStreamBranch.Empty, null, null),
            };
        }
    }

    /// <summary>Folds the uninstall stream's wire messages into the shared message shape.</summary>
    /// <param name="appId">The app to remove.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>One message per wire message, in order.</returns>
    private async IAsyncEnumerable<AppStreamMessage> UninstallMessagesAsync(
        string appId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new UninstallAppRequest { AppId = appId };

        await foreach (var response in _invoker.UninstallAppAsync(request, cancellationToken))
        {
            yield return response.ResultCase switch
            {
                UninstallAppResponse.ResultOneofCase.Progress => new AppStreamMessage(
                    AppStreamBranch.Progress, response.Progress, null),
                UninstallAppResponse.ResultOneofCase.Ok => new AppStreamMessage(AppStreamBranch.Completed, null, null),
                UninstallAppResponse.ResultOneofCase.Error => new AppStreamMessage(
                    AppStreamBranch.Error, null, response.Error),
                _ => new AppStreamMessage(AppStreamBranch.Empty, null, null),
            };
        }
    }

    /// <summary>
    /// Turns the folded messages of either direction into events, ending in exactly one terminal event,
    /// without guarding against the transport failing.
    /// </summary>
    /// <param name="messages">The folded wire messages.</param>
    /// <param name="operation">The client method the stream belongs to, for the logged failure.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>Progress events followed by exactly one terminal event.</returns>
    private async IAsyncEnumerable<AppOperationEvent> EventsAsync(
        IAsyncEnumerable<AppStreamMessage> messages,
        string operation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var message in messages)
        {
            // Checked here rather than left to the transport, so a caller that stopped watching gets
            // "you stopped watching" and not "the outcome was never reported", and so the client stops
            // pulling progress nobody is reading.
            if (cancellationToken.IsCancellationRequested)
            {
                yield return TerminalEvent(AppOperationEventKind.Cancelled, null);
                yield break;
            }

            if (message.Branch == AppStreamBranch.Progress)
            {
                yield return new AppOperationEvent(
                    AppOperationEventKind.Progress,
                    message.Progress!.Percent,
                    message.Progress.Stage,
                    null);
                continue;
            }

            if (message.Branch == AppStreamBranch.Completed)
            {
                yield return new AppOperationEvent(AppOperationEventKind.Completed, 100, string.Empty, null);
                yield break;
            }

            if (message.Branch == AppStreamBranch.Error)
            {
                yield return ToTerminalEvent(message.Error!, operation);
                yield break;
            }

            // A message carrying no branch at all is neither progress nor an outcome.
            yield return TerminalEvent(
                AppOperationEventKind.Failed,
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
            yield break;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            yield return TerminalEvent(AppOperationEventKind.Cancelled, null);
            yield break;
        }

        // The stream ended without the agent stating an outcome. An operation that stops mid-flight
        // leaves the panel not knowing whether the packages are present, so this is a truncation and
        // never a completion.
        yield return TerminalEvent(AppOperationEventKind.Truncated, null);
    }

    /// <summary>Turns the terminal error of a stream into the event that ends the sequence.</summary>
    /// <param name="error">The failure payload that closed the stream.</param>
    /// <param name="operation">The client method the stream belongs to, for the logged failure.</param>
    /// <returns>The dropped or idle ending where the agent named one, and a typed failure otherwise.</returns>
    private AppOperationEvent ToTerminalEvent(AgentError error, string operation)
    {
        if (error.Code == ErrorCode.StreamDropped)
        {
            return TerminalEvent(AppOperationEventKind.Dropped, null);
        }

        if (error.Code == ErrorCode.StreamIdle)
        {
            return TerminalEvent(AppOperationEventKind.Idle, null);
        }

        return TerminalEvent(
            AppOperationEventKind.Failed,
            AgentErrorTranslator.ToError(_logger, error, operation));
    }
}
