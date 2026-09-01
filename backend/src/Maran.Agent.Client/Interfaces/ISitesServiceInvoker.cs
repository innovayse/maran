using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.SitesService.AgentSitesClient"/> and the transport that performs
/// the <c>SitesService</c> calls, so the response-to-<see cref="Result{T}"/> mapping is testable
/// without a real gRPC channel — the same shape <see cref="IAccountsServiceInvoker"/> uses.
/// </summary>
internal interface ISitesServiceInvoker
{
    /// <summary>Invokes <c>CreateSite</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<CreateSiteResponse> CreateSiteAsync(CreateSiteRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>UpdateSitePhpVersion</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<UpdateSitePhpVersionResponse> UpdateSitePhpVersionAsync(
        UpdateSitePhpVersionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>EnableSite</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<EnableSiteResponse> EnableSiteAsync(EnableSiteRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>DisableSite</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DisableSiteResponse> DisableSiteAsync(DisableSiteRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>DeleteSite</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DeleteSiteResponse> DeleteSiteAsync(DeleteSiteRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes the server-streaming <c>TailSiteLog</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// The raw wire responses in order. The sequence ending without a terminal error message is the
    /// agent closing the stream normally.
    /// </returns>
    IAsyncEnumerable<TailSiteLogResponse> TailSiteLogAsync(
        TailSiteLogRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ReloadWebServer</c>.</summary>
    /// <param name="request">The wire request; it carries no fields.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ReloadWebServerResponse> ReloadWebServerAsync(
        ReloadWebServerRequest request,
        CancellationToken cancellationToken);
}
