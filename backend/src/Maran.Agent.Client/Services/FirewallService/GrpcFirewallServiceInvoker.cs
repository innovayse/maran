using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.FirewallService;

/// <summary>Production <see cref="IFirewallServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcFirewallServiceInvoker : IFirewallServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="IFirewallServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcFirewallServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.FirewallService.FirewallServiceClient Client
    {
        get { return new Maran.Agent.V1.FirewallService.FirewallServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<ListRulesResponse> ListRulesAsync(
        ListRulesRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListRulesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AllowPortResponse> AllowPortAsync(
        AllowPortRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.AllowPortAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DenyPortResponse> DenyPortAsync(
        DenyPortRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DenyPortAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BanAddressResponse> BanAddressAsync(
        BanAddressRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.BanAddressAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UnbanAddressResponse> UnbanAddressAsync(
        UnbanAddressRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.UnbanAddressAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListBansResponse> ListBansAsync(
        ListBansRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListBansAsync(request, cancellationToken: cancellationToken);
    }
}
