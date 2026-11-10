using Maran.Agent.Client.Services.TransferService;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the agent's connection to a registered remote server (minting the key it logs in
/// with, reading the host keys the server offers, testing the connection, reading the server's inventory;
/// every call read-only on the other host), and of the two rpcs that move one account's backup artifact
/// between Maran servers: the agent signs and reads it out, and verifies and publishes it in (issue #127).
/// The artifact rpcs open no network connection: the panel relays the bytes.
/// </summary>
/// <remarks>
/// <para>
/// A contract of its own, so that a module that reaches out of this server declares
/// <c>AgentCapability.Transfer</c> and gains nothing else. The agent opens an outbound SSH connection only
/// to the host an administrator registered, with one pinned host key, running a closed set of commands that
/// carry no caller-supplied text.
/// </para>
/// <para>
/// <b>Secrets.</b> The private key crosses this interface only inside <see cref="Maran.SharedKernel.Security.SensitiveString"/>, which
/// prints as a mask. The agent keeps nothing: the panel stores the key encrypted and sends it on every call
/// that connects.
/// </para>
/// <para>
/// <b>Failures.</b> A failed unary call carries the agent's code and, beside it, why the source refused
/// (<see cref="AgentSourceResult{T}.Reason"/>). A transport failure is answered by the resilience decorator
/// the Host registers over this contract as <c>AgentUnavailable</c>. The stream states every ending itself,
/// including the ones the agent did not state, as <see cref="AgentSourceInventoryFailed"/>; a cancellation
/// that interrupts a read in progress surfaces as <see cref="OperationCanceledException"/>.
/// </para>
/// </remarks>
public interface IAgentTransferClient
{
    /// <summary>Generates one ed25519 key pair for a remote source. No network is touched.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The key pair, or a typed failure. A response carrying neither a key nor an error, or a key missing
    /// either half, is <c>AgentInvalidResponse</c>, never a success.
    /// </returns>
    Task<AgentSourceResult<AgentGeneratedSourceKey>> GenerateSourceKeyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the host keys a remote host offers so the administrator can compare a fingerprint against the
    /// source's own console before anything is trusted. Nothing is authenticated.
    /// </summary>
    /// <param name="endpoint">Where to look. The user is not used.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The keys the host offered, in the agent's order of preference, or a typed failure. An answer with no
    /// key at all is <c>AgentInvalidResponse</c>: there is nothing the administrator could pin.
    /// </returns>
    Task<AgentSourceResult<IReadOnlyList<AgentScannedHostKey>>> ScanHostKeyAsync(
        AgentSourceEndpoint endpoint,
        CancellationToken cancellationToken);

    /// <summary>
    /// Connects to a registered source with its pinned host key and runs the fixed probes. Read-only on the
    /// source.
    /// </summary>
    /// <param name="endpoint">The source.</param>
    /// <param name="credential">The key to log in with and the host key to require.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// What the probes established, or a typed failure. A host key that differs from the pinned one is a
    /// failure whose reason is <see cref="SourceFailureReason.HostKeyChanged"/>.
    /// </returns>
    Task<AgentSourceResult<AgentTestedSource>> TestSourceAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the inventory of a registered source with a closed set of read-only commands and streams it
    /// account by account.
    /// </summary>
    /// <param name="endpoint">The source.</param>
    /// <param name="credential">The key to log in with and the host key to require.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// Progress, account and warning events followed by exactly one ending: <see cref="AgentSourceInventoryDone"/>
    /// or <see cref="AgentSourceInventoryFailed"/>. A stream that ended without the agent stating an outcome
    /// is a failure and never a completion.
    /// </returns>
    IAsyncEnumerable<AgentSourceInventoryEvent> ReadSourceInventoryAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one published, unsealed backup artifact out of the host together with a manifest the agent signs
    /// with the key supplied.
    /// </summary>
    /// <param name="accountUsername">System user name of the account whose backup is exported.</param>
    /// <param name="backupId">Id of the published backup, as minted when it was created.</param>
    /// <param name="signingKey">
    /// The per-server package signing key (PKCS#8 DER, base64-encoded). A secret held for the call only.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// The header, the artifact in bounded chunks and exactly one ending: <see cref="AgentBackupExportDone"/>
    /// or <see cref="AgentBackupExportFailed"/>. A stream that ended without the agent stating an outcome is
    /// a failure and never a completion.
    /// </returns>
    IAsyncEnumerable<AgentBackupExportEvent> ExportBackupAsync(
        string accountUsername,
        string backupId,
        Maran.SharedKernel.Security.SensitiveString signingKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hands one exported artifact to the agent, which verifies the signature, the operator's confirmed key
    /// fingerprint, the manifest and the digest before it publishes the artifact as a backup of this host.
    /// </summary>
    /// <param name="header">The signed manifest and the key fingerprint the operator confirmed.</param>
    /// <param name="chunks">The artifact, in order. Sent only after <paramref name="header"/>.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// <see cref="AgentBackupImported"/> when the artifact is published (or already was), or
    /// <see cref="AgentBackupImportRefused"/> carrying the code and why the package was refused. Nothing is
    /// published on a refusal.
    /// </returns>
    Task<AgentBackupImportResult> ImportBackupAsync(
        AgentBackupImportHeader header,
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks,
        CancellationToken cancellationToken);

    /// <summary>
    /// Copies one account's home from a registered source into this host with rsync over the pinned channel, in
    /// time-sliced runs (issue #197, phase 2). The stream ends with exactly one done or failed event.
    /// </summary>
    /// <param name="request">The pull.</param>
    /// <param name="cancellationToken">
    /// Cancels the pull: the agent stops at its next slice boundary and keeps the partial state, so calling again
    /// resumes. This is how the panel pauses and cancels.
    /// </param>
    /// <returns>The events.</returns>
    IAsyncEnumerable<AgentFilePullEvent> PullAccountFilesAsync(
        AgentFilePullRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Dumps one database of a registered source and loads it into an existing, empty database here through the
    /// restricted loader (issue #197, phase 2). The stream ends with exactly one done or failed event.
    /// </summary>
    /// <param name="request">The transfer.</param>
    /// <param name="cancellationToken">
    /// Stops listening; the agent runs the work to its end and cleans up regardless, because a load has no safe
    /// midpoint.
    /// </param>
    /// <returns>The events.</returns>
    IAsyncEnumerable<AgentDatabaseTransferEvent> StreamRemoteDatabaseAsync(
        AgentDatabaseTransferRequest request,
        CancellationToken cancellationToken);
}
