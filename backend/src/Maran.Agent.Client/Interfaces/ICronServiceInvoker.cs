using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.CronService.AgentCronClient"/> and the transport that performs
/// the <c>CronService</c> calls, so the response-to-<see cref="Result{T}"/> mapping is testable
/// without a real gRPC channel.
/// </summary>
internal interface ICronServiceInvoker
{
    /// <summary>Invokes <c>ListCronEntries</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListCronEntriesResponse> ListCronEntriesAsync(
        ListCronEntriesRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>CreateCronEntry</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<CreateCronEntryResponse> CreateCronEntryAsync(
        CreateCronEntryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>UpdateCronEntry</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<UpdateCronEntryResponse> UpdateCronEntryAsync(
        UpdateCronEntryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>DeleteCronEntry</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DeleteCronEntryResponse> DeleteCronEntryAsync(
        DeleteCronEntryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>SetCronEntryEnabled</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SetCronEntryEnabledResponse> SetCronEntryEnabledAsync(
        SetCronEntryEnabledRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>GetCronEntryOutput</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<GetCronEntryOutputResponse> GetCronEntryOutputAsync(
        GetCronEntryOutputRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>GetCronEnvironment</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<GetCronEnvironmentResponse> GetCronEnvironmentAsync(
        GetCronEnvironmentRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>SetCronEnvironment</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SetCronEnvironmentResponse> SetCronEnvironmentAsync(
        SetCronEnvironmentRequest request,
        CancellationToken cancellationToken);
}
