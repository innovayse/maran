using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.FirewallService;

/// <summary>Production <see cref="IFirewallServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcFirewallServiceInvoker : IFirewallServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.FirewallService.FirewallServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="IFirewallServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcFirewallServiceInvoker(Maran.Agent.V1.FirewallService.FirewallServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<ListRulesResponse> ListRulesAsync(
        ListRulesRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.ListRulesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AllowPortResponse> AllowPortAsync(
        AllowPortRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.AllowPortAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DenyPortResponse> DenyPortAsync(
        DenyPortRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.DenyPortAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BanAddressResponse> BanAddressAsync(
        BanAddressRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.BanAddressAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UnbanAddressResponse> UnbanAddressAsync(
        UnbanAddressRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.UnbanAddressAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListBansResponse> ListBansAsync(
        ListBansRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.ListBansAsync(request, cancellationToken: cancellationToken);
    }
}
