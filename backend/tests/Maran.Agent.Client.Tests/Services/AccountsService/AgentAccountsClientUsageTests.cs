using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;
using PanelReason = Maran.Agent.Client.Services.AccountsService.QuotaUnenforceableReason;
using WireReason = Maran.Agent.V1.QuotaUnenforceableReason;

namespace Maran.Agent.Client.Tests.Services.AccountsService;

/// <summary>
/// How an account's usage reading is projected: one quota case per wire state, no case invented for
/// a state the wire does not state, and the deprecated byte mirror never read as a stand-in.
/// </summary>
public sealed class AgentAccountsClientUsageTests
{
    /// <summary>An enforced quota carries its byte figure.</summary>
    [Fact]
    public async Task An_enforced_quota_carries_its_byte_figure()
    {
        var stub = Stub(ok =>
        {
            ok.QuotaState = QuotaState.Enforced;
#pragma warning disable CS0612 // The deprecated mirror is the only wire field carrying the enforced figure.
            ok.QuotaBytes = 4096;
#pragma warning restore CS0612
        });

        var result = await Client(stub).GetUsageAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1234ul, result.Value.UsedBytes);
        var enforced = Assert.IsType<AccountQuota.Enforced>(result.Value.Quota);
        Assert.Equal(4096ul, enforced.Bytes);
    }

    /// <summary>An enforceable filesystem with no limit set maps to its own case, carrying no figure.</summary>
    [Fact]
    public async Task An_enforceable_but_unset_quota_maps_to_its_own_case()
    {
        var stub = Stub(ok =>
        {
            ok.QuotaState = QuotaState.EnforceableButUnset;
        });

        var result = await Client(stub).GetUsageAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.IsType<AccountQuota.EnforceableButUnset>(result.Value.Quota);
    }

    /// <summary>Each named reason a quota cannot be enforced maps to its own panel reason.</summary>
    /// <param name="wire">The reason on the wire.</param>
    /// <param name="expected">The panel reason it must become.</param>
    [Theory]
    [InlineData(WireReason.MountedWithoutQuotaAccounting, PanelReason.MountedWithoutQuotaAccounting)]
    [InlineData(WireReason.AccountingNotEnabled, PanelReason.AccountingNotEnabled)]
    public async Task A_quota_that_cannot_be_enforced_keeps_the_reason_the_agent_gave(
        WireReason wire,
        PanelReason expected)
    {
        var stub = Stub(ok =>
        {
            ok.QuotaState = QuotaState.NotEnforceable;
            ok.QuotaUnenforceableReason = wire;
        });

        var result = await Client(stub).GetUsageAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var notEnforceable = Assert.IsType<AccountQuota.NotEnforceable>(result.Value.Quota);
        Assert.Equal(expected, notEnforceable.Reason);
    }

    /// <summary>
    /// An agent that predates the quota state sends UNSPECIFIED; the reading is refused rather than
    /// given a case, and the deprecated byte mirror — zero for two of the three real cases — is not
    /// read as a stand-in.
    /// </summary>
    [Fact]
    public async Task An_unspecified_quota_state_is_refused_rather_than_given_a_case()
    {
        var stub = Stub(ok =>
        {
            ok.QuotaState = QuotaState.Unspecified;
#pragma warning disable CS0612 // Set on purpose: the mirror must not rescue an unstated state.
            ok.QuotaBytes = 4096;
#pragma warning restore CS0612
        });

        var result = await Client(stub).GetUsageAsync("alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A NOT_ENFORCEABLE state with an absent or unknown reason is refused, not given a placeholder.</summary>
    /// <param name="reason">The unusable reason value.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task A_quota_that_cannot_be_enforced_without_a_nameable_reason_is_refused(int reason)
    {
        var stub = Stub(ok =>
        {
            ok.QuotaState = QuotaState.NotEnforceable;
            ok.QuotaUnenforceableReason = (WireReason)reason;
        });

        var result = await Client(stub).GetUsageAsync("alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A quota state from an agent newer than this panel is refused too.</summary>
    [Fact]
    public async Task A_quota_state_this_build_has_no_name_for_is_refused()
    {
        var stub = Stub(ok =>
        {
            ok.QuotaState = (QuotaState)99;
        });

        var result = await Client(stub).GetUsageAsync("alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Builds a stub answering one usage reading for 1234 used bytes, shaped by the caller.</summary>
    /// <param name="shape">Sets the quota fields of the success payload.</param>
    /// <returns>The stub transport.</returns>
    private static StubAccountsService Stub(Action<GetAccountUsageOk> shape)
    {
        var ok = new GetAccountUsageOk { UsedBytes = 1234 };
        shape(ok);

        return new StubAccountsService { UsageResponse = new GetAccountUsageResponse { Ok = ok } };
    }

    /// <summary>Builds the client under test over the stub.</summary>
    /// <param name="stub">The transport stub.</param>
    /// <returns>The client.</returns>
    private static AgentAccountsClient Client(StubAccountsService stub)
    {
        return new AgentAccountsClient(stub, NullLogger<AgentAccountsClient>.Instance);
    }
}
