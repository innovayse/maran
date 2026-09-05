using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.SitesService;

/// <summary>Stub of <c>ISitesServiceInvoker</c> returning canned responses and a canned stream.</summary>
/// <remarks>
/// It records the request of every rpc, because half of what these clients do is build the message:
/// a swapped account and domain, a dropped alias or an empty <c>php_version</c> is invisible in the
/// response mapping and destructive on the server.
/// </remarks>
internal sealed class StubSitesService : ISitesServiceInvoker
{
    /// <summary>Response returned from <see cref="CreateSiteAsync"/>.</summary>
    public CreateSiteResponse CreateResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="UpdateSitePhpVersionAsync"/>.</summary>
    public UpdateSitePhpVersionResponse UpdateResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="EnableSiteAsync"/>.</summary>
    public EnableSiteResponse EnableResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="DisableSiteAsync"/>.</summary>
    public DisableSiteResponse DisableResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="DeleteSiteAsync"/>.</summary>
    public DeleteSiteResponse DeleteResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="ReloadWebServerAsync"/>.</summary>
    public ReloadWebServerResponse ReloadResponse { get; set; } = new();

    /// <summary>How many times the batch reload was invoked.</summary>
    public int ReloadCallCount { get; private set; }

    /// <summary>The messages the tail stream yields before it ends.</summary>
    public List<TailSiteLogResponse> TailResponses { get; } = [];

    /// <summary>Invoked after each tail message is yielded, so a test can cancel mid-stream.</summary>
    public Action? OnTailYielded { get; set; }

    /// <summary>How many tail messages were actually pulled out of the stub.</summary>
    public int TailYieldedCount { get; private set; }

    /// <summary>The last request <see cref="CreateSiteAsync"/> received.</summary>
    public CreateSiteRequest? LastCreateRequest { get; private set; }

    /// <summary>The last request <see cref="UpdateSitePhpVersionAsync"/> received.</summary>
    public UpdateSitePhpVersionRequest? LastUpdateRequest { get; private set; }

    /// <summary>The last request <see cref="EnableSiteAsync"/> received.</summary>
    public EnableSiteRequest? LastEnableRequest { get; private set; }

    /// <summary>The last request <see cref="DisableSiteAsync"/> received.</summary>
    public DisableSiteRequest? LastDisableRequest { get; private set; }

    /// <summary>The last request <see cref="DeleteSiteAsync"/> received.</summary>
    public DeleteSiteRequest? LastDeleteRequest { get; private set; }

    /// <summary>The last request <see cref="TailSiteLogAsync"/> received.</summary>
    public TailSiteLogRequest? LastTailRequest { get; private set; }

    /// <inheritdoc/>
    public Task<CreateSiteResponse> CreateSiteAsync(CreateSiteRequest request, CancellationToken cancellationToken)
    {
        LastCreateRequest = request;
        return Task.FromResult(CreateResponse);
    }

    /// <inheritdoc/>
    public Task<UpdateSitePhpVersionResponse> UpdateSitePhpVersionAsync(
        UpdateSitePhpVersionRequest request,
        CancellationToken cancellationToken)
    {
        LastUpdateRequest = request;
        return Task.FromResult(UpdateResponse);
    }

    /// <inheritdoc/>
    public Task<EnableSiteResponse> EnableSiteAsync(EnableSiteRequest request, CancellationToken cancellationToken)
    {
        LastEnableRequest = request;
        return Task.FromResult(EnableResponse);
    }

    /// <inheritdoc/>
    public Task<DisableSiteResponse> DisableSiteAsync(DisableSiteRequest request, CancellationToken cancellationToken)
    {
        LastDisableRequest = request;
        return Task.FromResult(DisableResponse);
    }

    /// <inheritdoc/>
    public Task<DeleteSiteResponse> DeleteSiteAsync(DeleteSiteRequest request, CancellationToken cancellationToken)
    {
        LastDeleteRequest = request;
        return Task.FromResult(DeleteResponse);
    }

    /// <inheritdoc/>
    public Task<ReloadWebServerResponse> ReloadWebServerAsync(
        ReloadWebServerRequest request,
        CancellationToken cancellationToken)
    {
        ReloadCallCount++;
        return Task.FromResult(ReloadResponse);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TailSiteLogResponse> TailSiteLogAsync(
        TailSiteLogRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastTailRequest = request;

        foreach (var response in TailResponses)
        {
            await Task.Yield();
            TailYieldedCount++;
            yield return response;
            OnTailYielded?.Invoke();
        }
    }
}
