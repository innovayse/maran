using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.BackupService;

/// <summary>Stub of <c>IBackupServiceInvoker</c> returning canned responses and canned streams.</summary>
/// <remarks>
/// It can emit shapes the real agent never emits — a <c>readable</c> arm with no manifest, an entry
/// with no arm at all, a stream that stops without a terminal message. That is the point: the wire
/// permits them, so the client's refusal of them has to be exercised through something that can
/// produce them.
/// </remarks>
internal sealed class StubBackupService : IBackupServiceInvoker
{
    /// <summary>Response returned from <see cref="ListBackupsAsync"/>.</summary>
    public ListBackupsResponse ListResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="DeleteBackupAsync"/>.</summary>
    public DeleteBackupResponse DeleteResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="ProbeDestinationAsync"/>.</summary>
    public ProbeDestinationResponse ProbeResponse { get; set; } = new();

    /// <summary>The messages the create stream yields before it ends.</summary>
    public List<CreateBackupResponse> CreateResponses { get; } = [];

    /// <summary>The messages the restore stream yields before it ends.</summary>
    public List<RestoreBackupResponse> RestoreResponses { get; } = [];

    /// <summary>Invoked after each create message is yielded, so a test can cancel mid-stream.</summary>
    public Action? OnCreateYielded { get; set; }

    /// <summary>The last request <see cref="CreateBackupAsync"/> received.</summary>
    public CreateBackupRequest? LastCreateRequest { get; private set; }

    /// <summary>The last request <see cref="RestoreBackupAsync"/> received.</summary>
    public RestoreBackupRequest? LastRestoreRequest { get; private set; }

    /// <summary>The last request <see cref="ListBackupsAsync"/> received, or null when it was never called.</summary>
    public ListBackupsRequest? LastListRequest { get; private set; }

    /// <summary>The last request <see cref="DeleteBackupAsync"/> received.</summary>
    public DeleteBackupRequest? LastDeleteRequest { get; private set; }

    /// <summary>The last request <see cref="ProbeDestinationAsync"/> received.</summary>
    public ProbeDestinationRequest? LastProbeRequest { get; private set; }

    /// <inheritdoc/>
    public async IAsyncEnumerable<CreateBackupResponse> CreateBackupAsync(
        CreateBackupRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastCreateRequest = request;

        foreach (var response in CreateResponses)
        {
            await Task.Yield();
            yield return response;
            OnCreateYielded?.Invoke();
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<RestoreBackupResponse> RestoreBackupAsync(
        RestoreBackupRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastRestoreRequest = request;

        foreach (var response in RestoreResponses)
        {
            await Task.Yield();
            yield return response;
        }
    }

    /// <inheritdoc/>
    public Task<ListBackupsResponse> ListBackupsAsync(
        ListBackupsRequest request,
        CancellationToken cancellationToken)
    {
        LastListRequest = request;

        return Task.FromResult(ListResponse);
    }

    /// <inheritdoc/>
    public Task<DeleteBackupResponse> DeleteBackupAsync(
        DeleteBackupRequest request,
        CancellationToken cancellationToken)
    {
        LastDeleteRequest = request;

        return Task.FromResult(DeleteResponse);
    }

    /// <inheritdoc/>
    public Task<ProbeDestinationResponse> ProbeDestinationAsync(
        ProbeDestinationRequest request,
        CancellationToken cancellationToken)
    {
        LastProbeRequest = request;

        return Task.FromResult(ProbeResponse);
    }
}
