using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Wire = Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.TransferService;

/// <summary>Stub of <c>ITransferServiceInvoker</c> returning canned responses and a canned stream.</summary>
/// <remarks>
/// It can emit shapes the real agent never emits: a response with no arm, a stream that stops without a
/// terminal message, a stream that is torn by the transport. The wire permits them, so the client's refusal
/// of them has to be exercised through something that can produce them.
/// </remarks>
internal sealed class StubTransferService : ITransferServiceInvoker
{
    /// <summary>Response returned from <see cref="GenerateSourceKeyAsync"/>.</summary>
    public Wire.GenerateSourceKeyResponse GenerateResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="ScanHostKeyAsync"/>.</summary>
    public Wire.ScanHostKeyResponse ScanResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="TestSourceAsync"/>.</summary>
    public Wire.TestSourceResponse TestResponse { get; set; } = new();

    /// <summary>The messages the inventory stream yields before it ends.</summary>
    public List<Wire.ReadSourceInventoryResponse> InventoryResponses { get; } = [];

    /// <summary>When set, the inventory stream throws it after yielding every message, as a torn connection does.</summary>
    public Exception? InventoryFailure { get; set; }

    /// <summary>The messages the export stream yields before it ends.</summary>
    public List<Wire.ExportBackupResponse> ExportResponses { get; } = [];

    /// <summary>When set, the export stream throws it after yielding every message, as a torn connection does.</summary>
    public Exception? ExportFailure { get; set; }

    /// <summary>Response returned from <see cref="ImportBackupAsync"/>.</summary>
    public Wire.ImportBackupResponse ImportResponse { get; set; } = new();

    /// <summary>The wire messages the last import sent, in order.</summary>
    public List<Wire.ImportBackupRequest> ImportRequests { get; } = [];

    /// <summary>The last request <see cref="ExportBackupAsync"/> received.</summary>
    public Wire.ExportBackupRequest? LastExportRequest { get; private set; }

    /// <summary>The last request <see cref="ScanHostKeyAsync"/> received.</summary>
    public Wire.ScanHostKeyRequest? LastScanRequest { get; private set; }

    /// <summary>The last request <see cref="TestSourceAsync"/> received.</summary>
    public Wire.TestSourceRequest? LastTestRequest { get; private set; }

    /// <summary>The last request <see cref="ReadSourceInventoryAsync"/> received.</summary>
    public Wire.ReadSourceInventoryRequest? LastInventoryRequest { get; private set; }

    /// <inheritdoc/>
    public Task<Wire.GenerateSourceKeyResponse> GenerateSourceKeyAsync(
        Wire.GenerateSourceKeyRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(GenerateResponse);
    }

    /// <inheritdoc/>
    public Task<Wire.ScanHostKeyResponse> ScanHostKeyAsync(
        Wire.ScanHostKeyRequest request,
        CancellationToken cancellationToken)
    {
        LastScanRequest = request;

        return Task.FromResult(ScanResponse);
    }

    /// <inheritdoc/>
    public Task<Wire.TestSourceResponse> TestSourceAsync(
        Wire.TestSourceRequest request,
        CancellationToken cancellationToken)
    {
        LastTestRequest = request;

        return Task.FromResult(TestResponse);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Wire.ReadSourceInventoryResponse> ReadSourceInventoryAsync(
        Wire.ReadSourceInventoryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastInventoryRequest = request;

        foreach (var response in InventoryResponses)
        {
            await Task.Yield();
            yield return response;
        }

        if (InventoryFailure is not null)
        {
            throw InventoryFailure;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Wire.ExportBackupResponse> ExportBackupAsync(
        Wire.ExportBackupRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastExportRequest = request;

        foreach (var response in ExportResponses)
        {
            await Task.Yield();
            yield return response;
        }

        if (ExportFailure is not null)
        {
            throw ExportFailure;
        }
    }

    /// <inheritdoc/>
    public async Task<Wire.ImportBackupResponse> ImportBackupAsync(
        IAsyncEnumerable<Wire.ImportBackupRequest> requests,
        CancellationToken cancellationToken)
    {
        ImportRequests.Clear();

        await foreach (var request in requests.WithCancellation(cancellationToken))
        {
            ImportRequests.Add(request);
        }

        return ImportResponse;
    }

    /// <summary>The responses <see cref="PullAccountFilesAsync"/> yields, in order.</summary>
    public List<Wire.PullAccountFilesResponse> PullResponses { get; } = [];

    /// <summary>The responses <see cref="StreamRemoteDatabaseAsync"/> yields, in order.</summary>
    public List<Wire.StreamRemoteDatabaseResponse> DatabaseResponses { get; } = [];

    /// <summary>The last request <see cref="PullAccountFilesAsync"/> received.</summary>
    public Wire.PullAccountFilesRequest? LastPullRequest { get; private set; }

    /// <summary>The last request <see cref="StreamRemoteDatabaseAsync"/> received.</summary>
    public Wire.StreamRemoteDatabaseRequest? LastDatabaseRequest { get; private set; }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Wire.PullAccountFilesResponse> PullAccountFilesAsync(
        Wire.PullAccountFilesRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastPullRequest = request;

        foreach (var response in PullResponses)
        {
            await Task.Yield();
            yield return response;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Wire.StreamRemoteDatabaseResponse> StreamRemoteDatabaseAsync(
        Wire.StreamRemoteDatabaseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastDatabaseRequest = request;

        foreach (var response in DatabaseResponses)
        {
            await Task.Yield();
            yield return response;
        }
    }
}
