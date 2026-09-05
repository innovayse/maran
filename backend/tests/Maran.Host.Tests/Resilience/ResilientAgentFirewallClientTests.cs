using Maran.Agent.Client.Services.FirewallService;
using Maran.Host.Resilience;
using Polly.Timeout;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the firewall decorator does: every call goes through the pipeline, arguments unchanged.</summary>
/// <remarks>
/// Each method has its own retry test rather than one test standing for the class. A method that
/// forgets the pipeline is invisible from its call site and from every other method's test — this
/// repository has already shipped exactly that, one undecorated method inside a decorated class,
/// with the whole suite green.
///
/// The host's ssh ports are asserted on both mutations, because the decorator is the last code that
/// touches them before the client that refuses to send a ruleset without them: a decorator that
/// dropped the list would turn a working call into a refusal, and one that swapped two arguments
/// would render a policy around the wrong port.
/// </remarks>
public sealed class ResilientAgentFirewallClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The host's ssh ports, as a real host with two of them would report.</summary>
    private static readonly int[] SshPorts = [22, 2222];

    /// <summary>Listing retries a transport failure and forwards the ssh ports and the panel port.</summary>
    [Fact]
    public async Task Listing_retries_a_transport_failure_and_forwards_the_ssh_ports_and_the_panel_port()
    {
        var inner = new RecordingAgentFirewallClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).ListRulesAsync(SshPorts, 8443, default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Same(SshPorts, inner.LastSshPorts);
        Assert.Equal(8443, inner.LastPanelPort);
    }

    /// <summary>Allowing a port retries a transport failure and forwards every argument.</summary>
    [Fact]
    public async Task Allowing_a_port_retries_a_transport_failure_and_forwards_every_argument()
    {
        var inner = new RecordingAgentFirewallClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .AllowPortAsync(443, AgentFirewallProtocol.Tcp, "0.0.0.0/0", SshPorts, 8443, default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(443, inner.LastPort);
        Assert.Equal(AgentFirewallProtocol.Tcp, inner.LastProtocol);
        Assert.Equal("0.0.0.0/0", inner.LastSourceCidr);
        Assert.Same(SshPorts, inner.LastSshPorts);
        Assert.Equal(8443, inner.LastPanelPort);
    }

    /// <summary>Denying a port retries a transport failure and forwards every argument.</summary>
    [Fact]
    public async Task Denying_a_port_retries_a_transport_failure_and_forwards_every_argument()
    {
        var inner = new RecordingAgentFirewallClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .DenyPortAsync(3306, AgentFirewallProtocol.Udp, "10.0.0.0/8", SshPorts, 8443, default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(3306, inner.LastPort);
        Assert.Equal(AgentFirewallProtocol.Udp, inner.LastProtocol);
        Assert.Equal("10.0.0.0/8", inner.LastSourceCidr);
        Assert.Same(SshPorts, inner.LastSshPorts);
        Assert.Equal(8443, inner.LastPanelPort);
    }

    /// <summary>Banning retries a transport failure and forwards the address and the duration.</summary>
    [Fact]
    public async Task Banning_retries_a_transport_failure_and_forwards_the_address_and_the_duration()
    {
        var inner = new RecordingAgentFirewallClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .BanAsync("203.0.113.7", TimeSpan.FromHours(1), default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("203.0.113.7", inner.LastAddress);
        Assert.Equal(TimeSpan.FromHours(1), inner.LastTtl);
    }

    /// <summary>A permanent ban keeps its absent duration across the decorator.</summary>
    /// <remarks>
    /// Null is how permanent is expressed and 0 is how it is expressed on the wire, so a decorator
    /// that turned one into the other — or into a default TimeSpan — would change what the call
    /// means before the client that knows the difference ever sees it.
    /// </remarks>
    [Fact]
    public async Task A_permanent_ban_keeps_its_absent_duration_across_the_decorator()
    {
        var inner = new RecordingAgentFirewallClient();

        await Decorate(inner).BanAsync("203.0.113.7", null, default).WaitAsync(TestTimeout);

        Assert.Null(inner.LastTtl);
    }

    /// <summary>Unbanning retries a transport failure and forwards the address.</summary>
    [Fact]
    public async Task Unbanning_retries_a_transport_failure_and_forwards_the_address()
    {
        var inner = new RecordingAgentFirewallClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).UnbanAsync("203.0.113.7", default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("203.0.113.7", inner.LastAddress);
    }

    /// <summary>The ban listing retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task The_ban_listing_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentFirewallClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).ListBansAsync(default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>A rule listing that never returns is abandoned by the pipelines timeout.</summary>
    [Fact]
    public async Task A_rule_listing_that_never_returns_is_abandoned_by_the_pipelines_timeout()
    {
        var inner = new RecordingAgentFirewallClient { Hangs = true };

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await Decorate(inner).ListRulesAsync(SshPorts, 8443, default).WaitAsync(TestTimeout);
        });
    }

    /// <summary>Wraps the recording client in the decorator under the real pipeline.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorated client.</returns>
    private static ResilientAgentFirewallClient Decorate(RecordingAgentFirewallClient inner)
    {
        return new ResilientAgentFirewallClient(inner, OperationPipelineRegistry.WithOperationTimeout(1));
    }
}
