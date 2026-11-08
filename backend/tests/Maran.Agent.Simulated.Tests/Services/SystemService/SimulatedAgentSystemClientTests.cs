using Maran.Agent.Client.Services.SystemService;
using Maran.Agent.Simulated.Services.SystemService;

namespace Maran.Agent.Simulated.Tests.Services.SystemService;

/// <summary>The handshake of a host that is not there passes the panel's own check by value.</summary>
public sealed class SimulatedAgentSystemClientTests
{
    /// <summary>The revision reported is the newest the panel accepts, from the real handshake's constant.</summary>
    [Fact]
    public async Task The_handshake_reports_the_newest_contract_revision_the_panel_accepts()
    {
        var info = await new SimulatedAgentSystemClient().GetInfoAsync(CancellationToken.None);

        Assert.True(info.IsSuccess);
        Assert.Equal(AgentProtoVersionWindow.Maximum, info.Value.ProtoVersion);
        Assert.True(AgentProtoVersionWindow.Contains(info.Value.ProtoVersion));
    }

    /// <summary>The identity is a Debian-family host with an absolute backup root.</summary>
    [Fact]
    public async Task The_handshake_reports_an_identity_the_panel_can_use()
    {
        var info = await new SimulatedAgentSystemClient().GetInfoAsync(CancellationToken.None);

        Assert.Equal(AgentDistroFamily.Debian, info.Value.Family);
        Assert.Equal("ubuntu", info.Value.DistroId);
        Assert.Equal(SimulatedAgentSystemClient.SimulatedVersion, info.Value.Version);
        Assert.StartsWith("/", info.Value.BackupRoot, StringComparison.Ordinal);
    }

    /// <summary>A caller that has already given up is told so, as with a real transport.</summary>
    [Fact]
    public async Task A_cancelled_handshake_is_not_answered()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => { return new SimulatedAgentSystemClient().GetInfoAsync(cancelled.Token); });
    }
}
