using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.FirewallService;

/// <summary>Stub of <c>IFirewallServiceInvoker</c> returning canned responses and keeping every request.</summary>
/// <remarks>
/// Every captured request here is read by a test, and so is every "no request arrived": the client
/// refuses some calls before it sends them, and the only way to tell a refusal from a round trip is
/// that the stub was never entered.
/// </remarks>
internal sealed class StubFirewallService : IFirewallServiceInvoker
{
    /// <summary>Response returned from <see cref="ListRulesAsync"/>.</summary>
    public ListRulesResponse ListRulesResponse { get; set; } = new();

    /// <summary>The last rule listing the stub received, or null when the client sent none.</summary>
    public ListRulesRequest? LastListRulesRequest { get; private set; }

    /// <summary>Response returned from <see cref="AllowPortAsync"/>.</summary>
    public AllowPortResponse AllowResponse { get; set; } = new();

    /// <summary>The last allow the stub received, or null when the client sent none.</summary>
    public AllowPortRequest? LastAllowRequest { get; private set; }

    /// <summary>Response returned from <see cref="DenyPortAsync"/>.</summary>
    public DenyPortResponse DenyResponse { get; set; } = new();

    /// <summary>The last deny the stub received, or null when the client sent none.</summary>
    public DenyPortRequest? LastDenyRequest { get; private set; }

    /// <summary>Response returned from <see cref="BanAddressAsync"/>.</summary>
    public BanAddressResponse BanResponse { get; set; } = new();

    /// <summary>The last ban the stub received, or null when the client sent none.</summary>
    public BanAddressRequest? LastBanRequest { get; private set; }

    /// <summary>Response returned from <see cref="UnbanAddressAsync"/>.</summary>
    public UnbanAddressResponse UnbanResponse { get; set; } = new();

    /// <summary>The last unban the stub received, for asserting the mapping.</summary>
    public UnbanAddressRequest? LastUnbanRequest { get; private set; }

    /// <summary>Response returned from <see cref="ListBansAsync"/>.</summary>
    public ListBansResponse ListBansResponse { get; set; } = new();

    /// <summary>The last ban listing the stub received, for asserting one was made at all.</summary>
    public ListBansRequest? LastListBansRequest { get; private set; }

    /// <summary>Builds a stub whose allow fails with the agent's own words.</summary>
    /// <param name="code">The failure category the agent reports.</param>
    /// <param name="message">The agent's operator-facing sentence.</param>
    /// <returns>The configured stub.</returns>
    public static StubFirewallService FailingAllowWith(ErrorCode code, string message)
    {
        return new StubFirewallService
        {
            AllowResponse = new AllowPortResponse
            {
                Error = new AgentError { Code = code, Message = message },
            },
        };
    }

    /// <summary>Builds a stub whose allow succeeds, for asserting what was sent.</summary>
    /// <returns>The configured stub.</returns>
    public static StubFirewallService AcceptingAllow()
    {
        return new StubFirewallService
        {
            AllowResponse = new AllowPortResponse { Ok = new AllowPortOk() },
        };
    }

    /// <summary>Builds a stub whose deny succeeds, for asserting what was sent.</summary>
    /// <returns>The configured stub.</returns>
    public static StubFirewallService AcceptingDeny()
    {
        return new StubFirewallService
        {
            DenyResponse = new DenyPortResponse { Ok = new DenyPortOk() },
        };
    }

    /// <summary>Builds a stub whose ban succeeds, for asserting what was sent.</summary>
    /// <returns>The configured stub.</returns>
    public static StubFirewallService AcceptingBan()
    {
        return new StubFirewallService
        {
            BanResponse = new BanAddressResponse { Ok = new BanAddressOk() },
        };
    }

    /// <inheritdoc/>
    public Task<ListRulesResponse> ListRulesAsync(ListRulesRequest request, CancellationToken cancellationToken)
    {
        LastListRulesRequest = request;
        return Task.FromResult(ListRulesResponse);
    }

    /// <inheritdoc/>
    public Task<AllowPortResponse> AllowPortAsync(AllowPortRequest request, CancellationToken cancellationToken)
    {
        LastAllowRequest = request;
        return Task.FromResult(AllowResponse);
    }

    /// <inheritdoc/>
    public Task<DenyPortResponse> DenyPortAsync(DenyPortRequest request, CancellationToken cancellationToken)
    {
        LastDenyRequest = request;
        return Task.FromResult(DenyResponse);
    }

    /// <inheritdoc/>
    public Task<BanAddressResponse> BanAddressAsync(BanAddressRequest request, CancellationToken cancellationToken)
    {
        LastBanRequest = request;
        return Task.FromResult(BanResponse);
    }

    /// <inheritdoc/>
    public Task<UnbanAddressResponse> UnbanAddressAsync(
        UnbanAddressRequest request,
        CancellationToken cancellationToken)
    {
        LastUnbanRequest = request;
        return Task.FromResult(UnbanResponse);
    }

    /// <inheritdoc/>
    public Task<ListBansResponse> ListBansAsync(ListBansRequest request, CancellationToken cancellationToken)
    {
        LastListBansRequest = request;
        return Task.FromResult(ListBansResponse);
    }
}
