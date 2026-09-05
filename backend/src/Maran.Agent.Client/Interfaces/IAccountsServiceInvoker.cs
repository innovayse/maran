using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.AccountsService.AgentAccountsClient"/> and the transport that
/// performs the <c>AccountsService</c> calls, so the response-to-<see cref="Result{T}"/> mapping is
/// testable without a real gRPC channel — the same shape <c>ISystemServiceInvoker</c> uses.
/// </summary>
internal interface IAccountsServiceInvoker
{
    /// <summary>Invokes <c>CreateAccount</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<CreateAccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>SuspendAccount</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SuspendAccountResponse> SuspendAccountAsync(SuspendAccountRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>UnsuspendAccount</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<UnsuspendAccountResponse> UnsuspendAccountAsync(UnsuspendAccountRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>DeleteAccount</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DeleteAccountResponse> DeleteAccountAsync(DeleteAccountRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>SetAccountQuota</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SetAccountQuotaResponse> SetAccountQuotaAsync(SetAccountQuotaRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>GetAccountUsage</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<GetAccountUsageResponse> GetAccountUsageAsync(GetAccountUsageRequest request, CancellationToken cancellationToken);
}
