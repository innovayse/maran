using System.Runtime.CompilerServices;
using Grpc.Net.Client;
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

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentPhpClient(GrpcChannel channel, ILogger<AgentPhpClient> logger)
        : this(new GrpcPhpServiceInvoker(new V1.PhpService.PhpServiceClient(channel)), logger)
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
    public async IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(
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
                nameof(ErrorMessages.AgentInvalidResponse));
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
    /// The versions in the order the agent sent them, each keeping the tri-state default flag: an
    /// unset <c>is_default</c> stays null rather than becoming false, because the agent does not
    /// currently establish which version is the default.
    /// </returns>
    private static List<PhpVersionDto> ToVersions(ListPhpVersionsOk ok)
    {
        var versions = new List<PhpVersionDto>(ok.Versions.Count);
        foreach (var version in ok.Versions)
        {
            versions.Add(new PhpVersionDto(
                version.Version,
                version.FpmSocketDirectory,
                version.HasIsDefault ? version.IsDefault : null));
        }

        return versions;
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
            AgentErrorTranslator.ToError(_logger, error, nameof(InstallVersionAsync)).Code);
    }
}
