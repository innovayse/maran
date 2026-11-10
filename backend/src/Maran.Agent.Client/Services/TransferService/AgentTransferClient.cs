using Grpc.Net.Client;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging;
using WireSourceFailureReason = Maran.Agent.V1.SourceFailureReason;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>
/// Maps the agent's remote-source rpcs onto <see cref="AgentSourceResult{T}"/> and typed stream events.
/// </summary>
/// <remarks>
/// <para>
/// Same shape as the other agent clients: the failure branch of a response oneof becomes a typed
/// <see cref="Maran.SharedKernel.Results.Error"/> carrying only a code, and the agent's own diagnostic text
/// (which names hosts, paths and the output of the ssh client) is logged rather than returned
/// (rules/security.md item 8). The private key a call carries is handed to the translator as the secret this
/// call sent, so an agent that quotes a credential back cannot put it in the panel's log.
/// </para>
/// <para>
/// <b>What this file restores that the wire cannot guarantee.</b> A oneof makes the ARM exactly-one; it
/// does not make the payload in the arm usable. A key pair missing a half, an answer with no host key, a
/// quantity too large for the panel's type and an enum value this build has no member for are each refused
/// as <c>AgentInvalidResponse</c> and never repaired, because the panel would act on what it did not
/// receive. The inventory stream is held to the same standard: a stream that ends without a terminal
/// message is a failure, and the transport breaking is a failure.
/// </para>
/// </remarks>
public sealed class AgentTransferClient : IAgentTransferClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly ITransferServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since an error carries only a code.</summary>
    private readonly ILogger<AgentTransferClient> _logger;

    /// <summary>Reads the inventory stream into typed events.</summary>
    private readonly SourceInventoryStream _inventoryStream;

    /// <summary>Reads the artifact export stream into typed events.</summary>
    private readonly BackupExportStream _exportStream;

    /// <summary>Reads the pull of an account's files into typed events.</summary>
    private readonly FilePullStream _pullStream;

    /// <summary>Reads the transfer of one database into typed events.</summary>
    private readonly DatabaseTransferStream _databaseStream;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentTransferClient(ITransferServiceInvoker invoker, ILogger<AgentTransferClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
        _inventoryStream = new SourceInventoryStream(invoker, logger);
        _exportStream = new BackupExportStream(invoker, logger);
        _pullStream = new FilePullStream(invoker, logger);
        _databaseStream = new DatabaseTransferStream(invoker, logger);
    }

    /// <summary>Creates a client whose every call goes to the server the current agent target selects.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentTransferClient(IAgentChannels channels, ILogger<AgentTransferClient> logger)
        : this(new GrpcTransferServiceInvoker(channels), logger)
    {
    }

    /// <summary>Creates a client over one fixed <paramref name="channel"/>, whatever the target.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentTransferClient(GrpcChannel channel, ILogger<AgentTransferClient> logger)
        : this(new FixedAgentChannels(channel), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<AgentSourceResult<AgentGeneratedSourceKey>> GenerateSourceKeyAsync(
        CancellationToken cancellationToken)
    {
        var response = await _invoker.GenerateSourceKeyAsync(new GenerateSourceKeyRequest(), cancellationToken);

        switch (response.ResultCase)
        {
            case GenerateSourceKeyResponse.ResultOneofCase.Ok:
                var key = TransferMapper.ToGeneratedKey(response.Ok);
                return key is null
                    ? AgentSourceFailure.Invalid<AgentGeneratedSourceKey>()
                    : AgentSourceResult.Ok<AgentGeneratedSourceKey>(key);

            case GenerateSourceKeyResponse.ResultOneofCase.Error:
                return AgentSourceFailure.Translate<AgentGeneratedSourceKey>(
                    _logger,
                    response.Error,
                    WireSourceFailureReason.Unspecified,
                    nameof(GenerateSourceKeyAsync),
                    null);

            default:
                return AgentSourceFailure.Invalid<AgentGeneratedSourceKey>();
        }
    }

    /// <inheritdoc/>
    public async Task<AgentSourceResult<IReadOnlyList<AgentScannedHostKey>>> ScanHostKeyAsync(
        AgentSourceEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        var request = new ScanHostKeyRequest { Endpoint = TransferRequestMapper.ToWire(endpoint) };
        var response = await _invoker.ScanHostKeyAsync(request, cancellationToken);

        switch (response.ResultCase)
        {
            case ScanHostKeyResponse.ResultOneofCase.Ok:
                var keys = TransferMapper.ToScannedKeys(response.Ok);
                return keys is null
                    ? AgentSourceFailure.Invalid<IReadOnlyList<AgentScannedHostKey>>()
                    : AgentSourceResult.Ok<IReadOnlyList<AgentScannedHostKey>>(keys);

            case ScanHostKeyResponse.ResultOneofCase.Error:
                return AgentSourceFailure.Translate<IReadOnlyList<AgentScannedHostKey>>(
                    _logger,
                    response.Error,
                    response.FailureReason,
                    nameof(ScanHostKeyAsync),
                    null);

            default:
                return AgentSourceFailure.Invalid<IReadOnlyList<AgentScannedHostKey>>();
        }
    }

    /// <inheritdoc/>
    public async Task<AgentSourceResult<AgentTestedSource>> TestSourceAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        CancellationToken cancellationToken)
    {
        var request = new TestSourceRequest
        {
            Endpoint = TransferRequestMapper.ToWire(endpoint),
            Credential = TransferRequestMapper.ToWire(credential),
        };
        var response = await _invoker.TestSourceAsync(request, cancellationToken);

        switch (response.ResultCase)
        {
            case TestSourceResponse.ResultOneofCase.Ok:
                var tested = TransferMapper.ToTestedSource(response.Ok);
                return tested is null
                    ? AgentSourceFailure.Invalid<AgentTestedSource>()
                    : AgentSourceResult.Ok<AgentTestedSource>(tested);

            case TestSourceResponse.ResultOneofCase.Error:
                return AgentSourceFailure.Translate<AgentTestedSource>(
                    _logger,
                    response.Error,
                    response.FailureReason,
                    nameof(TestSourceAsync),
                    TransferRequestMapper.SecretOf(credential));

            default:
                return AgentSourceFailure.Invalid<AgentTestedSource>();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A transport failure (an agent that cannot be reached, a connection torn mid-stream) ends the stream
    /// in an <see cref="AgentSourceInventoryFailed"/> carrying the code of
    /// <see cref="AgentTransportFailure.ToError"/>, never in the raw exception.
    /// </remarks>
    public IAsyncEnumerable<AgentSourceInventoryEvent> ReadSourceInventoryAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        CancellationToken cancellationToken)
    {
        return AgentStreamGuard.EndOnTransportFailureAsync(
            _inventoryStream.EventsAsync(endpoint, credential, cancellationToken),
            error =>
            {
                return new AgentSourceInventoryFailed(error, SourceFailureReason.Unspecified);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A transport failure ends the stream in an <see cref="AgentFilePullFailed"/> with no reason, never in the
    /// raw exception.
    /// </remarks>
    public IAsyncEnumerable<AgentFilePullEvent> PullAccountFilesAsync(
        AgentFilePullRequest request,
        CancellationToken cancellationToken)
    {
        return AgentStreamGuard.EndOnTransportFailureAsync(
            _pullStream.EventsAsync(request, cancellationToken),
            error =>
            {
                return new AgentFilePullFailed(error, SourceFailureReason.Unspecified, TransferFailureReason.Unspecified);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A transport failure ends the stream in an <see cref="AgentDatabaseTransferFailed"/> with no reason, never
    /// in the raw exception.
    /// </remarks>
    public IAsyncEnumerable<AgentDatabaseTransferEvent> StreamRemoteDatabaseAsync(
        AgentDatabaseTransferRequest request,
        CancellationToken cancellationToken)
    {
        return AgentStreamGuard.EndOnTransportFailureAsync(
            _databaseStream.EventsAsync(request, cancellationToken),
            error =>
            {
                return new AgentDatabaseTransferFailed(
                    error,
                    SourceFailureReason.Unspecified,
                    TransferFailureReason.Unspecified);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A transport failure ends the stream in an <see cref="AgentBackupExportFailed"/> carrying the code of
    /// <see cref="AgentTransportFailure.ToError"/>, never in the raw exception.
    /// </remarks>
    public IAsyncEnumerable<AgentBackupExportEvent> ExportBackupAsync(
        string accountUsername,
        string backupId,
        Maran.SharedKernel.Security.SensitiveString signingKey,
        CancellationToken cancellationToken)
    {
        return AgentStreamGuard.EndOnTransportFailureAsync(
            _exportStream.EventsAsync(accountUsername, backupId, signingKey, cancellationToken),
            error =>
            {
                return new AgentBackupExportFailed(error);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AgentBackupImportResult> ImportBackupAsync(
        AgentBackupImportHeader header,
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks,
        CancellationToken cancellationToken)
    {
        var requests = BackupTransferMapper.ToWireAsync(header, chunks, cancellationToken);
        var response = await _invoker.ImportBackupAsync(requests, cancellationToken);

        switch (response.ResultCase)
        {
            case ImportBackupResponse.ResultOneofCase.Ok:
                var imported = BackupTransferMapper.ToImported(response.Ok);
                return imported is null
                    ? new AgentBackupImportRefused(AgentSourceFailure.InvalidResponse(), BackupImportRefusal.Unspecified)
                    : imported;

            case ImportBackupResponse.ResultOneofCase.Error:
                return new AgentBackupImportRefused(
                    AgentErrorTranslator.ToError(_logger, response.Error, nameof(ImportBackupAsync)),
                    BackupTransferMapper.ToRefusal(response.RefusalReason));

            default:
                return new AgentBackupImportRefused(AgentSourceFailure.InvalidResponse(), BackupImportRefusal.Unspecified);
        }
    }
}
