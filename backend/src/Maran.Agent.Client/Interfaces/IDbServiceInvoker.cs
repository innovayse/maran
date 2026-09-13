using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.DbService.AgentDbClient"/> and the transport that performs the
/// <c>DbService</c> calls, so the response-to-<see cref="Result{T}"/> mapping is testable without a
/// real gRPC channel.
/// </summary>
internal interface IDbServiceInvoker
{
    /// <summary>Invokes <c>CreateDatabase</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<CreateDatabaseResponse> CreateDatabaseAsync(
        CreateDatabaseRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>DropDatabase</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DropDatabaseResponse> DropDatabaseAsync(
        DropDatabaseRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>SetDatabasePassword</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SetDatabasePasswordResponse> SetDatabasePasswordAsync(
        SetDatabasePasswordRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ListDatabases</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListDatabasesResponse> ListDatabasesAsync(
        ListDatabasesRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>GetDatabaseSize</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<GetDatabaseSizeResponse> GetDatabaseSizeAsync(
        GetDatabaseSizeRequest request,
        CancellationToken cancellationToken);
}
