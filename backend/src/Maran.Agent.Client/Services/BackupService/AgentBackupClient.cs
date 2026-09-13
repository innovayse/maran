using System.Runtime.CompilerServices;
using Grpc.Net.Client;
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

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentBackupClient(IBackupServiceInvoker invoker, ILogger<AgentBackupClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentBackupClient(GrpcChannel channel, ILogger<AgentBackupClient> logger)
        : this(new GrpcBackupServiceInvoker(new V1.BackupService.BackupServiceClient(channel)), logger)
    {
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BackupCreateEvent> CreateAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new CreateBackupRequest
        {
            AccountUsername = accountUsername,
            BackupId = backupId,
            Destination = ToWireDestination(destination),
        };

        await foreach (var response in _invoker.CreateBackupAsync(request, cancellationToken))
        {
            // Checked here rather than left to the transport, so a caller that stopped watching gets
            // "you stopped watching" and not "the outcome was never reported".
            if (cancellationToken.IsCancellationRequested)
            {
                yield return CreateEvent(BackupCreateEventKind.Cancelled);
                yield break;
            }

            if (response.ResultCase == CreateBackupResponse.ResultOneofCase.Progress)
            {
                yield return new BackupCreateEvent(
                    BackupCreateEventKind.Progress,
                    response.Progress.Percent,
                    response.Progress.Stage,
                    0,
                    string.Empty,
                    0,
                    null);
                continue;
            }

            if (response.ResultCase == CreateBackupResponse.ResultOneofCase.Ok)
            {
                yield return new BackupCreateEvent(
                    BackupCreateEventKind.Created,
                    100,
                    string.Empty,
                    response.Ok.SizeBytes,
                    response.Ok.Sha256,
                    response.Ok.DatabaseCount,
                    null);
                yield break;
            }

            if (response.ResultCase == CreateBackupResponse.ResultOneofCase.Error)
            {
                yield return ToTerminalCreateEvent(response.Error, destination);
                yield break;
            }

            // A message carrying no branch at all is neither progress nor an outcome.
            yield return new BackupCreateEvent(
                BackupCreateEventKind.Failed,
                0,
                string.Empty,
                0,
                string.Empty,
                0,
                nameof(ErrorMessages.AgentInvalidResponse));
            yield break;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            yield return CreateEvent(BackupCreateEventKind.Cancelled);
            yield break;
        }

        // The stream ended without the agent stating an outcome. There may or may not be an artifact
        // on the destination, so this is reported as a truncation and never as a completion.
        yield return CreateEvent(BackupCreateEventKind.Truncated);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BackupRestoreEvent> RestoreAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new RestoreBackupRequest
        {
            AccountUsername = accountUsername,
            BackupId = backupId,
            Destination = ToWireDestination(destination),
            ExpectedSha256 = expectedSha256,
        };
        request.AllowedDatabases.AddRange(allowedDatabases);

        await foreach (var response in _invoker.RestoreBackupAsync(request, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return RestoreEvent(BackupRestoreEventKind.Cancelled);
                yield break;
            }

            if (response.ResultCase == RestoreBackupResponse.ResultOneofCase.Progress)
            {
                yield return new BackupRestoreEvent(
                    BackupRestoreEventKind.Progress,
                    response.Progress.Percent,
                    response.Progress.Stage,
                    null,
                    null);
                continue;
            }

            if (response.ResultCase == RestoreBackupResponse.ResultOneofCase.Ok)
            {
                yield return new BackupRestoreEvent(
                    BackupRestoreEventKind.Restored,
                    100,
                    string.Empty,
                    new AgentRestoreOutcome(
                        response.Ok.FilesRestored,
                        response.Ok.DatabasesRestored,
                        response.Ok.DatabasesTotal),
                    null);
                yield break;
            }

            if (response.ResultCase == RestoreBackupResponse.ResultOneofCase.Error)
            {
                yield return ToTerminalRestoreEvent(response.Error, destination);
                yield break;
            }

            yield return new BackupRestoreEvent(
                BackupRestoreEventKind.Failed,
                0,
                string.Empty,
                null,
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
            yield break;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            yield return RestoreEvent(BackupRestoreEventKind.Cancelled);
            yield break;
        }

        yield return RestoreEvent(BackupRestoreEventKind.Truncated);
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
        if (destination.Kind != AgentBackupDestinationKind.Local)
        {
            return Result<IReadOnlyList<AgentBackupSummary>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentNotImplemented), ErrorType.Failure));
        }

        var request = new ListBackupsRequest { AccountUsername = accountUsername };
        var response = await _invoker.ListBackupsAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ListBackupsResponse.ResultOneofCase.Ok => ToSummariesResult(response.Ok),
            ListBackupsResponse.ResultOneofCase.Error => Result<IReadOnlyList<AgentBackupSummary>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListAsync), destination.S3SecretAccessKey)),
            _ => Result<IReadOnlyList<AgentBackupSummary>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        var request = new DeleteBackupRequest
        {
            AccountUsername = accountUsername,
            BackupId = backupId,
            Destination = ToWireDestination(destination),
        };
        var response = await _invoker.DeleteBackupAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DeleteBackupResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            DeleteBackupResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DeleteAsync), destination.S3SecretAccessKey)),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentPublicReadVerdict>> ProbeAsync(
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        var request = new ProbeDestinationRequest { Destination = ToWireDestination(destination) };
        var response = await _invoker.ProbeDestinationAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ProbeDestinationResponse.ResultOneofCase.Ok => ToVerdictResult(response.Ok),
            ProbeDestinationResponse.ResultOneofCase.Error => Result<AgentPublicReadVerdict>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ProbeAsync), destination.S3SecretAccessKey)),
            _ => Result<AgentPublicReadVerdict>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>Builds a terminal create event with no payload, for the endings that carry none.</summary>
    /// <param name="kind">Which ending this is.</param>
    /// <returns>The event that closes the sequence.</returns>
    private static BackupCreateEvent CreateEvent(BackupCreateEventKind kind)
    {
        return new BackupCreateEvent(kind, 0, string.Empty, 0, string.Empty, 0, null);
    }

    /// <summary>Builds a terminal restore event with no payload, for the endings that carry none.</summary>
    /// <param name="kind">Which ending this is.</param>
    /// <returns>The event that closes the sequence.</returns>
    private static BackupRestoreEvent RestoreEvent(BackupRestoreEventKind kind)
    {
        return new BackupRestoreEvent(kind, 0, string.Empty, null, null);
    }

    /// <summary>Projects a panel destination onto the wire message, revealing the secrets it holds.</summary>
    /// <param name="destination">The destination the caller named.</param>
    /// <returns>The wire message, which never leaves this project.</returns>
    /// <remarks>
    /// The two credentials are revealed here and nowhere else in this client, which is what makes
    /// "where does the secret escape" a one-search question. There is no local-versus-S3 branch: the
    /// panel's record already carries empty values for the members a local destination has none of,
    /// and a branch that blanked them would be a second place the shape of a local destination is
    /// decided.
    /// </remarks>
    private static BackupDestination ToWireDestination(AgentBackupDestination destination)
    {
        return new BackupDestination
        {
            Kind = destination.Kind == AgentBackupDestinationKind.S3
                ? BackupDestinationKind.S3
                : BackupDestinationKind.Local,
            Path = destination.Path,
            S3Bucket = destination.S3Bucket,
            S3Region = destination.S3Region,
            S3Endpoint = destination.S3Endpoint,
            S3AccessKeyId = destination.S3AccessKeyId.Reveal(),
            S3SecretAccessKey = destination.S3SecretAccessKey.Reveal(),
            S3PathStyle = destination.S3PathStyle,
        };
    }

    /// <summary>Projects the wire listing onto the panel's summaries, refusing an entry the contract forbids.</summary>
    /// <param name="ok">The success payload of <c>ListBackups</c>.</param>
    /// <returns>
    /// The summaries in the order the agent sent them, or an invalid-response failure when an entry
    /// claims to be readable and carries no manifest.
    /// </returns>
    /// <remarks>
    /// The unset-arm case fails for the same reason and by the same rule: proto3 lets a oneof carry
    /// no arm at all, and an entry that says neither "here is what it holds" nor "here is why I
    /// cannot say" is not an entry a caller can act on.
    /// </remarks>
    private static Result<IReadOnlyList<AgentBackupSummary>> ToSummariesResult(ListBackupsOk ok)
    {
        var summaries = new List<AgentBackupSummary>(ok.Backups.Count);
        foreach (var backup in ok.Backups)
        {
            if (backup.StateCase == BackupInfo.StateOneofCase.Readable)
            {
                if (backup.Readable.Manifest is null)
                {
                    return Result<IReadOnlyList<AgentBackupSummary>>.Fail(
                        Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
                }

                summaries.Add(new AgentBackupSummary(
                    backup.BackupId,
                    new AgentReadableBackup(
                        ToManifest(backup.Readable.Manifest),
                        backup.Readable.ArtifactBytes,
                        backup.Readable.ArtifactSha256),
                    null));
                continue;
            }

            if (backup.StateCase == BackupInfo.StateOneofCase.Unreadable)
            {
                summaries.Add(new AgentBackupSummary(
                    backup.BackupId,
                    null,
                    new AgentBackupUnreadableReason(
                        ToUnreadableKind(backup.Unreadable.Kind),
                        backup.Unreadable.Version)));
                continue;
            }

            return Result<IReadOnlyList<AgentBackupSummary>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
        }

        return Result<IReadOnlyList<AgentBackupSummary>>.Ok(summaries);
    }

    /// <summary>Projects a wire manifest onto the panel's own.</summary>
    /// <param name="manifest">The manifest the sidecar carried.</param>
    /// <returns>The panel-side manifest, database lines included.</returns>
    private static AgentBackupManifest ToManifest(BackupManifest manifest)
    {
        var databases = new List<AgentManifestDatabase>(manifest.Databases.Count);
        foreach (var database in manifest.Databases)
        {
            databases.Add(new AgentManifestDatabase(database.Name, database.Bytes, database.Sha256));
        }

        return new AgentBackupManifest(
            manifest.Version,
            manifest.Account,
            manifest.BackupId,
            manifest.CreatedAtUnix,
            manifest.HomeBytes,
            databases,
            manifest.AgentVersion);
    }

    /// <summary>Maps the wire's unreadable kind onto the panel's.</summary>
    /// <param name="kind">The kind the agent reported.</param>
    /// <returns>
    /// The matching member, and <see cref="AgentBackupUnreadableKind.Unknown"/> for the wire's
    /// unspecified value and for a kind a newer agent added — never a guess at one of the three.
    /// </returns>
    private static AgentBackupUnreadableKind ToUnreadableKind(UnreadableKind kind)
    {
        return kind switch
        {
            UnreadableKind.Corrupt => AgentBackupUnreadableKind.Corrupt,
            UnreadableKind.UnknownVersion => AgentBackupUnreadableKind.UnknownVersion,
            UnreadableKind.NotARegularFile => AgentBackupUnreadableKind.NotARegularFile,
            _ => AgentBackupUnreadableKind.Unknown,
        };
    }

    /// <summary>Turns the terminal error of a create stream into the event that ends the sequence.</summary>
    /// <param name="error">The failure payload that closed the stream.</param>
    /// <param name="destination">The destination this call named, for redacting its secret from the log.</param>
    /// <returns>The dropped or idle ending where the agent named one, and a typed failure otherwise.</returns>
    private BackupCreateEvent ToTerminalCreateEvent(AgentError error, AgentBackupDestination destination)
    {
        if (error.Code == ErrorCode.StreamDropped)
        {
            return CreateEvent(BackupCreateEventKind.Dropped);
        }

        if (error.Code == ErrorCode.StreamIdle)
        {
            return CreateEvent(BackupCreateEventKind.Idle);
        }

        return new BackupCreateEvent(
            BackupCreateEventKind.Failed,
            0,
            string.Empty,
            0,
            string.Empty,
            0,
            AgentErrorTranslator.ToError(_logger, error, nameof(CreateAsync), destination.S3SecretAccessKey).Code);
    }

    /// <summary>Turns the terminal error of a restore stream into the event that ends the sequence.</summary>
    /// <param name="error">The failure payload that closed the stream.</param>
    /// <param name="destination">The destination this call named, for redacting its secret from the log.</param>
    /// <returns>The dropped or idle ending where the agent named one, and a typed failure otherwise.</returns>
    /// <remarks>
    /// The rolled-back and not-rolled-back database lists ride on this error's text. They are logged
    /// with it and not returned, because they are operator-facing detail naming a customer's database
    /// objects; the panel records the failure code and the operator reads the log line beside it.
    /// </remarks>
    private BackupRestoreEvent ToTerminalRestoreEvent(AgentError error, AgentBackupDestination destination)
    {
        if (error.Code == ErrorCode.StreamDropped)
        {
            return RestoreEvent(BackupRestoreEventKind.Dropped);
        }

        if (error.Code == ErrorCode.StreamIdle)
        {
            return RestoreEvent(BackupRestoreEventKind.Idle);
        }

        return new BackupRestoreEvent(
            BackupRestoreEventKind.Failed,
            0,
            string.Empty,
            null,
            AgentErrorTranslator.ToError(_logger, error, nameof(RestoreAsync), destination.S3SecretAccessKey));
    }

    /// <summary>Projects the probe's success payload onto a verdict, logging why nothing was established.</summary>
    /// <param name="ok">The success payload of <c>ProbeDestination</c>.</param>
    /// <returns>
    /// The verdict the agent stated, and <see cref="AgentPublicReadVerdict.Unspecified"/> for a value
    /// this build has no member for. The unproven reason is logged rather than returned: it is the
    /// agent's own sentence about a network failure, and it is operator-facing.
    /// </returns>
    private Result<AgentPublicReadVerdict> ToVerdictResult(ProbeDestinationOk ok)
    {
        var verdict = ok.Verdict switch
        {
            PublicReadVerdict.Private => AgentPublicReadVerdict.Private,
            PublicReadVerdict.PubliclyReadable => AgentPublicReadVerdict.PubliclyReadable,
            PublicReadVerdict.Unproven => AgentPublicReadVerdict.Unproven,
            _ => AgentPublicReadVerdict.Unspecified,
        };

        if (verdict == AgentPublicReadVerdict.Unproven)
        {
            LogUnprovenProbe(_logger, ok.UnprovenReason, null);
        }

        return Result<AgentPublicReadVerdict>.Ok(verdict);
    }
}
