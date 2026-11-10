using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.TransferService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>
/// The agent's transfer client with its answers set by the test, recording every call it receives — including the
/// credential, so a test can assert what the panel sent and, as importantly, what it did not send.
/// </summary>
public sealed class ScriptedTransferClient : IAgentTransferClient
{
    /// <summary>A fingerprint of the first scanned key.</summary>
    public const string FingerprintA = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    /// <summary>A fingerprint of the second scanned key.</summary>
    public const string FingerprintB = "SHA256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    /// <summary>The private key document the fake agent "generates".</summary>
    public const string GeneratedPrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nGENERATED-PRIVATE-KEY-SENTINEL\n-----END OPENSSH PRIVATE KEY-----\n";

    /// <summary>The public key line the fake agent "generates".</summary>
    public const string GeneratedPublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIgenerated";

    /// <summary>The answer to <c>GenerateSourceKeyAsync</c>.</summary>
    public AgentSourceResult<AgentGeneratedSourceKey> Generated { get; set; } =
        AgentSourceResult.Ok<AgentGeneratedSourceKey>(new AgentGeneratedSourceKey(
            GeneratedPublicKey, new SensitiveString(GeneratedPrivateKey), "SHA256:GENERATED"));

    /// <summary>The answer to <c>ScanHostKeyAsync</c>.</summary>
    public AgentSourceResult<IReadOnlyList<AgentScannedHostKey>> Scanned { get; set; } =
        AgentSourceResult.Ok<IReadOnlyList<AgentScannedHostKey>>(
        [
            new AgentScannedHostKey("ssh-ed25519", "ssh-ed25519 AAAAed", FingerprintA),
            new AgentScannedHostKey("ssh-rsa", "ssh-rsa AAAArsa", FingerprintB),
        ]);

    /// <summary>The answer to <c>TestSourceAsync</c>.</summary>
    public AgentSourceResult<AgentTestedSource> Tested { get; set; } =
        AgentSourceResult.Ok<AgentTestedSource>(new AgentTestedSource(
            IsRoot: false,
            HomeFreeBytes: 1_000,
            HomeTotalBytes: 5_000,
            SystemRelease: "Debian GNU/Linux 12",
            Kind: SourceKind.GenericLinux,
            MaranVersion: null,
            DatabaseServer: SourceDatabaseServer.MariaDb,
            WebServer: SourceWebServer.Nginx,
            MailFound: true));

    /// <summary>The events <c>ReadSourceInventoryAsync</c> yields, in order.</summary>
    public IReadOnlyList<AgentSourceInventoryEvent> Inventory { get; set; } =
        [new AgentSourceInventoryDone(0, 0)];

    /// <summary>The events <c>ExportBackupAsync</c> yields, in order.</summary>
    public IReadOnlyList<AgentBackupExportEvent> Export { get; set; } = [new AgentBackupExportDone(0)];

    /// <summary>What <c>ImportBackupAsync</c> answers.</summary>
    public AgentBackupImportResult ImportResult { get; set; } =
        new AgentBackupImported("backup", 1, new string('a', 64), 0, false);

    /// <summary>The backups exported, in order: account name and backup id.</summary>
    public List<(string Account, string BackupId)> ExportCalls { get; } = [];

    /// <summary>The bytes the last import received, joined.</summary>
    public byte[] ImportedBytes { get; private set; } = [];

    /// <summary>The header the last import carried.</summary>
    public AgentBackupImportHeader? ImportedHeader { get; private set; }

    /// <summary>How many times a key was generated.</summary>
    public int GenerateCalls { get; private set; }

    /// <summary>The endpoints scanned, in order.</summary>
    public List<AgentSourceEndpoint> ScannedEndpoints { get; } = [];

    /// <summary>The endpoints and credentials tested, in order.</summary>
    public List<(AgentSourceEndpoint Endpoint, AgentSourceCredential Credential)> TestedCalls { get; } = [];

    /// <summary>The endpoints and credentials whose inventory was read, in order.</summary>
    public List<(AgentSourceEndpoint Endpoint, AgentSourceCredential Credential)> InventoryCalls { get; } = [];

    /// <summary>Counts the call and answers <see cref="Generated"/>.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The scripted answer.</returns>
    public Task<AgentSourceResult<AgentGeneratedSourceKey>> GenerateSourceKeyAsync(CancellationToken cancellationToken)
    {
        GenerateCalls++;
        return Task.FromResult(Generated);
    }

    /// <summary>Records the endpoint and answers <see cref="Scanned"/>.</summary>
    /// <param name="endpoint">Where to look.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The scripted answer.</returns>
    public Task<AgentSourceResult<IReadOnlyList<AgentScannedHostKey>>> ScanHostKeyAsync(
        AgentSourceEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ScannedEndpoints.Add(endpoint);
        return Task.FromResult(Scanned);
    }

    /// <summary>Records the call and answers <see cref="Tested"/>.</summary>
    /// <param name="endpoint">The source.</param>
    /// <param name="credential">The key and host key sent.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The scripted answer.</returns>
    public Task<AgentSourceResult<AgentTestedSource>> TestSourceAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        CancellationToken cancellationToken)
    {
        TestedCalls.Add((endpoint, credential));
        return Task.FromResult(Tested);
    }

    /// <summary>Records the call and yields <see cref="Inventory"/>.</summary>
    /// <param name="endpoint">The source.</param>
    /// <param name="credential">The key and host key sent.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The scripted events.</returns>
    public async IAsyncEnumerable<AgentSourceInventoryEvent> ReadSourceInventoryAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        InventoryCalls.Add((endpoint, credential));

        foreach (var inventoryEvent in Inventory)
        {
            await Task.Yield();
            yield return inventoryEvent;
        }
    }

    /// <summary>A failed unary answer that carries the reason the source refused.</summary>
    /// <typeparam name="T">The answer's value type.</typeparam>
    /// <param name="reason">Why the source refused.</param>
    /// <returns>The failure.</returns>
    public static AgentSourceResult<T> Failure<T>(SourceFailureReason reason)
    {
        return AgentSourceResult.Fail<T>(Error.Of("AgentSystemFailure", ErrorType.Failure), reason);
    }

    /// <summary>A failed unary answer that says the agent could not be reached.</summary>
    /// <typeparam name="T">The answer's value type.</typeparam>
    /// <returns>The failure.</returns>
    public static AgentSourceResult<T> AgentDown<T>()
    {
        return AgentSourceResult.Fail<T>(Error.Of("AgentUnavailable", ErrorType.Unavailable));
    }

    /// <summary>Records the export and yields the scripted events.</summary>
    /// <param name="accountUsername">The account.</param>
    /// <param name="backupId">The backup.</param>
    /// <param name="signingKey">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The scripted events.</returns>
    public async IAsyncEnumerable<AgentBackupExportEvent> ExportBackupAsync(
        string accountUsername,
        string backupId,
        Maran.SharedKernel.Security.SensitiveString signingKey,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ExportCalls.Add((accountUsername, backupId));

        foreach (var exportEvent in Export)
        {
            await Task.Yield();
            yield return exportEvent;
        }
    }

    /// <summary>Records the header and drains the chunks.</summary>
    /// <param name="header">The header.</param>
    /// <param name="chunks">The artifact.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The scripted result.</returns>
    public async Task<AgentBackupImportResult> ImportBackupAsync(
        AgentBackupImportHeader header,
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks,
        CancellationToken cancellationToken)
    {
        ImportedHeader = header;
        var received = new List<byte>();

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            received.AddRange(chunk.ToArray());
        }

        ImportedBytes = [.. received];

        return ImportResult;
    }

    /// <summary>The events <c>PullAccountFilesAsync</c> yields, in order.</summary>
    public IReadOnlyList<AgentFilePullEvent> FilePull { get; set; } = [new AgentFilePullDone(1, null)];

    /// <summary>The events <c>StreamRemoteDatabaseAsync</c> yields, in order.</summary>
    public IReadOnlyList<AgentDatabaseTransferEvent> DatabaseTransfer { get; set; } =
        [new AgentDatabaseTransferDone(0)];

    /// <summary>The pulls requested, in order.</summary>
    public List<AgentFilePullRequest> PullCalls { get; } = [];

    /// <summary>The database transfers requested, in order.</summary>
    public List<AgentDatabaseTransferRequest> DatabaseCalls { get; } = [];

    /// <summary>Records the call and yields <see cref="FilePull"/>.</summary>
    /// <param name="request">The pull.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The scripted events.</returns>
    public async IAsyncEnumerable<AgentFilePullEvent> PullAccountFilesAsync(
        AgentFilePullRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        PullCalls.Add(request);

        foreach (var pullEvent in FilePull)
        {
            await Task.Yield();
            yield return pullEvent;
        }
    }

    /// <summary>Records the call and yields <see cref="DatabaseTransfer"/>.</summary>
    /// <param name="request">The transfer.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The scripted events.</returns>
    public async IAsyncEnumerable<AgentDatabaseTransferEvent> StreamRemoteDatabaseAsync(
        AgentDatabaseTransferRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        DatabaseCalls.Add(request);

        foreach (var transferEvent in DatabaseTransfer)
        {
            await Task.Yield();
            yield return transferEvent;
        }
    }
}
