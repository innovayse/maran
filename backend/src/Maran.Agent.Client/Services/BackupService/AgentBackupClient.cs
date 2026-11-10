using Grpc.Net.Client;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Maps the agent's backup rpcs onto <see cref="Result{T}"/> and typed stream events.</summary>
/// <remarks>
/// <para>
/// Same shape as the other agent clients: the failure branch of a response oneof becomes a typed
/// <see cref="Error"/> carrying only a code, and the agent's own diagnostic text — which names paths
/// on the host and, on a failed restore, the databases it rolled back — is logged rather than
/// returned (rules/security.md item 8). The destination's secret access key is handed to the
/// translator as the secret this call sent, so an agent that quotes a rejected credential back
/// cannot put it in the panel's log.
/// </para>
/// <para>
/// <b>The one guarantee this file restores, and nothing else can.</b> <c>BackupInfo.state</c> is a
/// oneof, which makes the ARM exactly-one; it does not make <c>ReadableBackup.manifest</c> present.
/// The agent's own <c>BackupSummary</c> is a Rust sum type, so "readable with no manifest" cannot
/// exist there and two named agent tests exhibit that — but the wire permits it, and a reader that
/// assumed otherwise would hand the panel a summary describing a backup nobody described. So
/// <see cref="ListAsync"/> refuses that shape: an entry claiming to be readable and carrying no
/// manifest fails the whole listing as an invalid response. The whole listing rather than that entry,
/// because it is not damage the agent reports — it is the agent breaking its contract, and a caller
/// that received a shortened list would prune, restore and report against it as though it were
/// complete.
/// </para>
/// </remarks>
public sealed class AgentBackupClient : IAgentBackupClient
{
    /// <summary>Pre-compiled log delegate for a probe that established nothing.</summary>
    private static readonly Action<ILogger, string, Exception?> LogUnprovenProbe =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(AgentBackupClient)),
            "Backup destination probe established nothing: {UnprovenReason}");

    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IBackupServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentBackupClient> _logger;

    /// <summary>Reads the create stream into typed events.</summary>
    private readonly BackupCreateStream _createStream;

    /// <summary>Reads the restore stream into typed events.</summary>
    private readonly BackupRestoreStream _restoreStream;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentBackupClient(IBackupServiceInvoker invoker, ILogger<AgentBackupClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
        _createStream = new BackupCreateStream(invoker, logger);
        _restoreStream = new BackupRestoreStream(invoker, logger);
    }

    /// <summary>Creates a client whose every call goes to the server the current agent target selects.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentBackupClient(IAgentChannels channels, ILogger<AgentBackupClient> logger)
        : this(new GrpcBackupServiceInvoker(channels), logger)
    {
    }

    /// <summary>Creates a client over one fixed <paramref name="channel"/>, whatever the target.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentBackupClient(GrpcChannel channel, ILogger<AgentBackupClient> logger)
        : this(new FixedAgentChannels(channel), logger)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A transport failure — an agent that cannot be reached, a connection torn mid-stream — ends the
    /// stream in a <see cref="BackupCreateEventKind.Failed"/> event carrying the code of
    /// <see cref="AgentTransportFailure.ToError"/>, never in the raw exception.
    /// </remarks>
    public IAsyncEnumerable<BackupCreateEvent> CreateAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        return AgentStreamGuard.EndOnTransportFailureAsync(
            _createStream.EventsAsync(accountUsername, backupId, destination, cancellationToken),
            error =>
            {
                return BackupStreamEvents.CreateFailed(error.Code);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A transport failure ends the stream in a <see cref="BackupRestoreEventKind.Failed"/> event
    /// carrying <see cref="AgentTransportFailure.ToError"/>, never in the raw exception.
    /// </remarks>
    public IAsyncEnumerable<BackupRestoreEvent> RestoreAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        AgentRestoreScope scope,
        CancellationToken cancellationToken)
    {
        return AgentStreamGuard.EndOnTransportFailureAsync(
            _restoreStream.EventsAsync(
                accountUsername,
                backupId,
                destination,
                expectedSha256,
                allowedDatabases,
                scope,
                cancellationToken),
            error =>
            {
                return new BackupRestoreEvent(
                    BackupRestoreEventKind.Failed,
                    0,
                    string.Empty,
                    null,
                    error);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentBackupSummary>>> ListAsync(
        string accountUsername,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        // The rpc carries no destination: the agent lists its own root-only directory, which is why
        // a per-call subdirectory is refused everywhere else in this contract. The parameter is on
        // the panel's surface so that a caller states which destination it believes it is reading,
        // and a destination this agent cannot serve is refused here rather than answered from the
        // local directory — the silent substitution this whole client is written to avoid.
        if (destination is not AgentLocalBackupDestination)
        {
            return Result<IReadOnlyList<AgentBackupSummary>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentNotImplemented), ErrorType.Unavailable));
        }

        var request = new ListBackupsRequest { AccountUsername = accountUsername };
        var response = await _invoker.ListBackupsAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ListBackupsResponse.ResultOneofCase.Ok => BackupListingMapper.ToSummariesResult(response.Ok),
            ListBackupsResponse.ResultOneofCase.Error => Result<IReadOnlyList<AgentBackupSummary>>.Fail(
                AgentErrorTranslator.ToError(
                    _logger,
                    response.Error,
                    nameof(ListAsync),
                    BackupDestinationMapper.SecretOf(destination))),
            _ => Result<IReadOnlyList<AgentBackupSummary>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentBackupVerification>> VerifyBackupAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        var request = new VerifyBackupRequest
        {
            AccountUsername = accountUsername,
            BackupId = backupId,
            Destination = BackupDestinationMapper.ToWireDestination(destination, includeArchiveKey: true),
        };
        var response = await _invoker.VerifyBackupAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            VerifyBackupResponse.ResultOneofCase.Ok => Result<AgentBackupVerification>.Ok(
                new AgentBackupVerification(
                    response.Ok.MemberCount,
                    response.Ok.DatabaseCount,
                    response.Ok.ArtifactBytes)),
            VerifyBackupResponse.ResultOneofCase.Error => Result<AgentBackupVerification>.Fail(
                AgentErrorTranslator.ToError(
                    _logger,
                    response.Error,
                    nameof(VerifyBackupAsync),
                    BackupDestinationMapper.SecretOf(destination))),
            _ => Result<AgentBackupVerification>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        var request = new DeleteBackupRequest
        {
            AccountUsername = accountUsername,
            BackupId = backupId,
            Destination = BackupDestinationMapper.ToWireDestination(destination, includeArchiveKey: false),
        };
        var response = await _invoker.DeleteBackupAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DeleteBackupResponse.ResultOneofCase.Ok => Result.Ok(),
            DeleteBackupResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(
                    _logger,
                    response.Error,
                    nameof(DeleteAsync),
                    BackupDestinationMapper.SecretOf(destination))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentPublicReadVerdict>> ProbeAsync(
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        var request = new ProbeDestinationRequest
        {
            Destination = BackupDestinationMapper.ToWireDestination(destination, includeArchiveKey: false),
        };
        var response = await _invoker.ProbeDestinationAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ProbeDestinationResponse.ResultOneofCase.Ok => ToVerdictResult(response.Ok),
            ProbeDestinationResponse.ResultOneofCase.Error => Result<AgentPublicReadVerdict>.Fail(
                AgentErrorTranslator.ToError(
                    _logger,
                    response.Error,
                    nameof(ProbeAsync),
                    BackupDestinationMapper.SecretOf(destination))),
            _ => Result<AgentPublicReadVerdict>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>Projects the probe's success payload onto a verdict, logging why nothing was established.</summary>
    /// <param name="ok">The success payload of <c>ProbeDestination</c>.</param>
    /// <returns>
    /// The verdict the agent stated, or an <c>AgentInvalidResponse</c> failure for a value this build
    /// has no member for — never a guess, because only a private verdict may save a destination. The
    /// unproven reason is logged rather than returned: it is the agent's own sentence about a
    /// network failure, and it is operator-facing.
    /// </returns>
    private Result<AgentPublicReadVerdict> ToVerdictResult(ProbeDestinationOk ok)
    {
        AgentPublicReadVerdict verdict;
        switch (ok.Verdict)
        {
            case PublicReadVerdict.Private:
                verdict = AgentPublicReadVerdict.Private;
                break;

            case PublicReadVerdict.PubliclyReadable:
                verdict = AgentPublicReadVerdict.PubliclyReadable;
                break;

            case PublicReadVerdict.Unproven:
                verdict = AgentPublicReadVerdict.Unproven;
                LogUnprovenProbe(_logger, ok.UnprovenReason, null);
                break;

            default:
                return Result<AgentPublicReadVerdict>.Fail(
                    Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
        }

        return Result<AgentPublicReadVerdict>.Ok(verdict);
    }
}
