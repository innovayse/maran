using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.FirewallService.AgentFirewallClient"/> and the transport that
/// performs the <c>FirewallService</c> calls, so the response-to-<see cref="Result{T}"/> mapping is
/// testable without a real gRPC channel.
/// </summary>
internal interface IFirewallServiceInvoker
{
    /// <summary>Invokes <c>ListRules</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListRulesResponse> ListRulesAsync(
        ListRulesRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>AllowPort</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<AllowPortResponse> AllowPortAsync(
        AllowPortRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>DenyPort</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DenyPortResponse> DenyPortAsync(
        DenyPortRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>BanAddress</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<BanAddressResponse> BanAddressAsync(
        BanAddressRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>UnbanAddress</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<UnbanAddressResponse> UnbanAddressAsync(
        UnbanAddressRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ListBans</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListBansResponse> ListBansAsync(
        ListBansRequest request,
        CancellationToken cancellationToken);
}
