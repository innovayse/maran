using System.Runtime.CompilerServices;
using Grpc.Net.Client;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.PhpService;

/// <summary>Maps the agent's PHP rpcs onto <see cref="Result{T}"/> and typed stream events.</summary>
/// <remarks>
/// Same shape as the other agent clients for the unary call. The install rpc is server-streaming and
/// always ends in a stated way: the agent's own success or failure, one of the two stream endings it
/// reports, or — when the transport simply stopped — a truncation, which is not a success.
/// </remarks>
public sealed class AgentPhpClient : IAgentPhpClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IPhpServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentPhpClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentPhpClient(IPhpServiceInvoker invoker, ILogger<AgentPhpClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client whose every call goes to the server the current agent target selects.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentPhpClient(IAgentChannels channels, ILogger<AgentPhpClient> logger)
        : this(new GrpcPhpServiceInvoker(channels), logger)
    {
    }

    /// <summary>Creates a client over one fixed <paramref name="channel"/>, whatever the target.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentPhpClient(GrpcChannel channel, ILogger<AgentPhpClient> logger)
        : this(new FixedAgentChannels(channel), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<PhpVersionDto>>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.ListPhpVersionsAsync(new ListPhpVersionsRequest(), cancellationToken);

        return response.ResultCase switch
        {
            ListPhpVersionsResponse.ResultOneofCase.Ok => Result<IReadOnlyList<PhpVersionDto>>.Ok(
                ToVersions(response.Ok)),
            ListPhpVersionsResponse.ResultOneofCase.Error => Result<IReadOnlyList<PhpVersionDto>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListVersionsAsync))),
            _ => Result<IReadOnlyList<PhpVersionDto>>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<InstallablePhpVersionDto>>> ListInstallableVersionsAsync(
        CancellationToken cancellationToken)
    {
        var response = await _invoker.ListInstallablePhpVersionsAsync(
            new ListInstallablePhpVersionsRequest(),
            cancellationToken);

        return response.ResultCase switch
        {
            ListInstallablePhpVersionsResponse.ResultOneofCase.Ok => Result<IReadOnlyList<InstallablePhpVersionDto>>.Ok(
                ToInstallable(response.Ok)),
            ListInstallablePhpVersionsResponse.ResultOneofCase.Error => Result<IReadOnlyList<InstallablePhpVersionDto>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListInstallableVersionsAsync))),
            _ => Result<IReadOnlyList<InstallablePhpVersionDto>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A transport failure ends the stream in a <see cref="PhpInstallEventKind.Failed"/> event carrying
    /// the error of <see cref="AgentTransportFailure.ToError"/>, kind included, never in the raw exception.
    /// </remarks>
    public IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(string version, CancellationToken cancellationToken)
    {
        return AgentStreamGuard.EndOnTransportFailureAsync(
            InstallEventsAsync(version, cancellationToken),
            error =>
            {
                return new PhpInstallEvent(
                    PhpInstallEventKind.Failed,
                    0,
                    string.Empty,
                    string.Empty,
                    error);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<PhpExtensionDto>>> ListExtensionsAsync(
        string version,
        CancellationToken cancellationToken)
    {
        var response = await _invoker.ListPhpExtensionsAsync(
            new ListPhpExtensionsRequest { Version = version },
            cancellationToken);

        return response.ResultCase switch
        {
            ListPhpExtensionsResponse.ResultOneofCase.Ok => Result<IReadOnlyList<PhpExtensionDto>>.Ok(
                PhpExtensionMapper.ToExtensions(response.Ok)),
            ListPhpExtensionsResponse.ResultOneofCase.Error => Result<IReadOnlyList<PhpExtensionDto>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListExtensionsAsync))),
            _ => Result<IReadOnlyList<PhpExtensionDto>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A transport failure ends the stream in a <see cref="PhpExtensionEventKind.Failed"/> event carrying
    /// the error of <see cref="AgentTransportFailure.ToError"/>, kind included, never in the raw exception.
    /// </remarks>
    public IAsyncEnumerable<PhpExtensionEvent> SetExtensionAsync(
        string version,
        string extension,
        bool enabled,
        CancellationToken cancellationToken)
    {
        return AgentStreamGuard.EndOnTransportFailureAsync(
            ExtensionEventsAsync(version, extension, enabled, cancellationToken),
            error =>
            {
                return TerminalEvent(PhpExtensionEventKind.Failed, error);
            },
            cancellationToken);
    }

    /// <summary>Streams the install events the agent states, without guarding against the transport failing.</summary>
    /// <param name="version">The PHP version to install.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>Progress events followed by exactly one terminal event.</returns>
    private async IAsyncEnumerable<PhpInstallEvent> InstallEventsAsync(
        string version,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new InstallPhpVersionRequest { Version = version };

        await foreach (var response in _invoker.InstallPhpVersionAsync(request, cancellationToken))
        {
            // Checked here rather than left to the transport, so a caller that stopped watching gets
            // "you stopped watching" and not "the outcome was never reported" — and so the client
            // stops pulling progress nobody is reading.
            if (cancellationToken.IsCancellationRequested)
            {
                yield return new PhpInstallEvent(
                    PhpInstallEventKind.Cancelled,
                    0,
                    string.Empty,
                    string.Empty,
                    null);
                yield break;
            }

            if (response.ResultCase == InstallPhpVersionResponse.ResultOneofCase.Progress)
            {
                yield return new PhpInstallEvent(
                    PhpInstallEventKind.Progress,
                    response.Progress.Percent,
                    response.Progress.Stage,
                    string.Empty,
                    null);
                continue;
            }

            if (response.ResultCase == InstallPhpVersionResponse.ResultOneofCase.Ok)
            {
                yield return new PhpInstallEvent(
                    PhpInstallEventKind.Installed,
                    100,
                    string.Empty,
                    response.Ok.Version,
                    null);
                yield break;
            }

            if (response.ResultCase == InstallPhpVersionResponse.ResultOneofCase.Error)
            {
                yield return ToTerminalEvent(response.Error);
                yield break;
            }

            // A message carrying no branch at all is neither progress nor an outcome.
            yield return new PhpInstallEvent(
                PhpInstallEventKind.Failed,
                0,
                string.Empty,
                string.Empty,
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
            yield break;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            yield return new PhpInstallEvent(PhpInstallEventKind.Cancelled, 0, string.Empty, string.Empty, null);
            yield break;
        }

        // The stream ended without the agent stating an outcome. Unlike a log tail, whose natural
        // end is silence, an install that stops mid-flight leaves the panel not knowing whether the
        // version is present, so this is reported as a truncation rather than as a completion.
        yield return new PhpInstallEvent(PhpInstallEventKind.Truncated, 0, string.Empty, string.Empty, null);
    }

    /// <summary>Projects the wire version list onto the panel's DTOs.</summary>
    /// <param name="ok">The success payload of <c>ListPhpVersions</c>.</param>
    /// <returns>
    /// The versions in the order the agent sent them, each carrying its default status: an unset
    /// <c>is_default</c> becomes <see cref="AgentPhpDefaultStatus.Unknown"/> rather than
    /// <see cref="AgentPhpDefaultStatus.NotDefault"/>, because the agent does not currently
    /// establish which version is the default.
    /// </returns>
    private static List<PhpVersionDto> ToVersions(ListPhpVersionsOk ok)
    {
        var versions = new List<PhpVersionDto>(ok.Versions.Count);
        foreach (var version in ok.Versions)
        {
            versions.Add(new PhpVersionDto(
                version.Version,
                version.FpmSocketDirectory,
                ToDefaultStatus(version)));
        }

        return versions;
    }

    /// <summary>Projects the wire installable-version list onto the panel's DTOs.</summary>
    /// <param name="ok">The success payload of <c>ListInstallablePhpVersions</c>.</param>
    /// <returns>The versions in the order the agent sent them, newest first.</returns>
    private static List<InstallablePhpVersionDto> ToInstallable(ListInstallablePhpVersionsOk ok)
    {
        var versions = new List<InstallablePhpVersionDto>(ok.Versions.Count);
        foreach (var version in ok.Versions)
        {
            versions.Add(new InstallablePhpVersionDto(version.Version, version.Installed));
        }

        return versions;
    }

    /// <summary>Reads the wire's explicit-presence <c>is_default</c> as one of the three answers it can mean.</summary>
    /// <param name="version">The wire version.</param>
    /// <returns>
    /// <see cref="AgentPhpDefaultStatus.Unknown"/> when the field is unset, otherwise
    /// <see cref="AgentPhpDefaultStatus.Default"/> or <see cref="AgentPhpDefaultStatus.NotDefault"/>
    /// as the agent stated.
    /// </returns>
    private static AgentPhpDefaultStatus ToDefaultStatus(PhpVersion version)
    {
        if (!version.HasIsDefault)
        {
            return AgentPhpDefaultStatus.Unknown;
        }

        return version.IsDefault ? AgentPhpDefaultStatus.Default : AgentPhpDefaultStatus.NotDefault;
    }

    /// <summary>Turns the terminal error of an install stream into the event that ends the sequence.</summary>
    /// <param name="error">The failure payload that closed the stream.</param>
    /// <returns>The dropped or idle ending where the agent named one, and a typed failure otherwise.</returns>
    private PhpInstallEvent ToTerminalEvent(AgentError error)
    {
        if (error.Code == ErrorCode.StreamDropped)
        {
            return new PhpInstallEvent(PhpInstallEventKind.Dropped, 0, string.Empty, string.Empty, null);
        }

        if (error.Code == ErrorCode.StreamIdle)
        {
            return new PhpInstallEvent(PhpInstallEventKind.Idle, 0, string.Empty, string.Empty, null);
        }

        return new PhpInstallEvent(
            PhpInstallEventKind.Failed,
            0,
            string.Empty,
            string.Empty,
            AgentErrorTranslator.ToError(_logger, error, nameof(InstallVersionAsync)));
    }

    /// <summary>Streams the extension-change events the agent states, without guarding against the transport failing.</summary>
    /// <param name="version">The PHP version to change.</param>
    /// <param name="extension">The catalogue name of the extension.</param>
    /// <param name="enabled">The state to put the extension in.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>Progress events followed by exactly one terminal event.</returns>
    private async IAsyncEnumerable<PhpExtensionEvent> ExtensionEventsAsync(
        string version,
        string extension,
        bool enabled,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new SetPhpExtensionRequest { Version = version, Extension = extension, Enabled = enabled };

        await foreach (var response in _invoker.SetPhpExtensionAsync(request, cancellationToken))
        {
            // Checked here rather than left to the transport, so a caller that stopped watching gets
            // "you stopped watching" and not "the outcome was never reported" — and so the client
            // stops pulling progress nobody is reading.
            if (cancellationToken.IsCancellationRequested)
            {
                yield return TerminalEvent(PhpExtensionEventKind.Cancelled, null);
                yield break;
            }

            if (response.ResultCase == SetPhpExtensionResponse.ResultOneofCase.Progress)
            {
                yield return new PhpExtensionEvent(
                    PhpExtensionEventKind.Progress,
                    response.Progress.Percent,
                    response.Progress.Stage,
                    string.Empty,
                    string.Empty,
                    false,
                    null);
                continue;
            }

            if (response.ResultCase == SetPhpExtensionResponse.ResultOneofCase.Ok)
            {
                yield return new PhpExtensionEvent(
                    PhpExtensionEventKind.Changed,
                    100,
                    string.Empty,
                    response.Ok.Version,
                    response.Ok.Extension,
                    response.Ok.Enabled,
                    null);
                yield break;
            }

            if (response.ResultCase == SetPhpExtensionResponse.ResultOneofCase.Error)
            {
                yield return ToExtensionTerminalEvent(response.Error);
                yield break;
            }

            // A message carrying no branch at all is neither progress nor an outcome.
            yield return TerminalEvent(
                PhpExtensionEventKind.Failed,
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
            yield break;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            yield return TerminalEvent(PhpExtensionEventKind.Cancelled, null);
            yield break;
        }

        // The stream ended without the agent stating an outcome. A change that stops mid-flight
        // leaves the panel not knowing whether the extension is loaded, so this is reported as a
        // truncation rather than as a completion.
        yield return TerminalEvent(PhpExtensionEventKind.Truncated, null);
    }

    /// <summary>Turns the terminal error of an extension-change stream into the event that ends the sequence.</summary>
    /// <param name="error">The failure payload that closed the stream.</param>
    /// <returns>The dropped or idle ending where the agent named one, and a typed failure otherwise.</returns>
    private PhpExtensionEvent ToExtensionTerminalEvent(AgentError error)
    {
        if (error.Code == ErrorCode.StreamDropped)
        {
            return TerminalEvent(PhpExtensionEventKind.Dropped, null);
        }

        if (error.Code == ErrorCode.StreamIdle)
        {
            return TerminalEvent(PhpExtensionEventKind.Idle, null);
        }

        return TerminalEvent(
            PhpExtensionEventKind.Failed,
            AgentErrorTranslator.ToError(_logger, error, nameof(SetExtensionAsync)));
    }

    /// <summary>Builds a terminal event that carries no progress and no echoed outcome.</summary>
    /// <param name="kind">How the stream ended.</param>
    /// <param name="error">The typed failure for a failed ending; null for every other.</param>
    /// <returns>The terminal event.</returns>
    private static PhpExtensionEvent TerminalEvent(PhpExtensionEventKind kind, Error? error)
    {
        return new PhpExtensionEvent(kind, 0, string.Empty, string.Empty, string.Empty, false, error);
    }

    /// <inheritdoc/>
    public async Task<Result> ApplyPoolSettingsAsync(
        string accountUsername,
        string version,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken)
    {
        var request = new ApplyPhpPoolSettingsRequest
        {
            AccountUsername = accountUsername,
            Version = version,
            MaxChildren = maxChildren,
        };
        foreach (var setting in settingOverrides)
        {
            request.Overrides.Add(new PhpSetting { Name = setting.Name, Value = setting.Value });
        }

        var response = await _invoker.ApplyPhpPoolSettingsAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ApplyPhpPoolSettingsResponse.ResultOneofCase.Ok => Result.Ok(),
            ApplyPhpPoolSettingsResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ApplyPoolSettingsAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }
}
