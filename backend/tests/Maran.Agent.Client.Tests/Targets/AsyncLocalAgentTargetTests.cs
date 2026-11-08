using Maran.Agent.Client.Targets;
using Maran.SharedKernel.Constants;

namespace Maran.Agent.Client.Tests.Targets;

/// <summary>How the current agent target is chosen, nested, restored and kept apart between concurrent flows.</summary>
public sealed class AsyncLocalAgentTargetTests
{
    /// <summary>A node id used wherever a test needs a server that is not the local one.</summary>
    private static readonly Guid NodeA = new("11111111-1111-1111-1111-111111111111");

    /// <summary>A second node id.</summary>
    private static readonly Guid NodeB = new("22222222-2222-2222-2222-222222222222");

    /// <summary>With no scope open the target is the local server, so nothing changes for a caller that never selects.</summary>
    [Fact]
    public void With_no_scope_open_the_target_is_the_local_server()
    {
        var target = new AsyncLocalAgentTarget();

        Assert.Equal(FleetServers.LocalId, target.Current);
    }

    /// <summary>An open scope addresses the chosen server and closing it puts the local default back.</summary>
    [Fact]
    public void An_open_scope_addresses_its_server_and_closing_it_restores_the_default()
    {
        var target = new AsyncLocalAgentTarget();

        using (target.Begin(NodeA))
        {
            Assert.Equal(NodeA, target.Current);
        }

        Assert.Equal(FleetServers.LocalId, target.Current);
    }

    /// <summary>Scopes nest, and closing the inner one restores the outer one, not the default.</summary>
    [Fact]
    public void Closing_an_inner_scope_restores_the_outer_scope()
    {
        var target = new AsyncLocalAgentTarget();

        using (target.Begin(NodeA))
        {
            using (target.Begin(NodeB))
            {
                Assert.Equal(NodeB, target.Current);
            }

            Assert.Equal(NodeA, target.Current);
        }
    }

    /// <summary>Disposing a scope twice restores once: a late second dispose cannot undo a newer scope.</summary>
    [Fact]
    public void Disposing_a_scope_twice_does_not_disturb_a_newer_scope()
    {
        var target = new AsyncLocalAgentTarget();
        var first = target.Begin(NodeA);
        first.Dispose();

        using (target.Begin(NodeB))
        {
            first.Dispose();

            Assert.Equal(NodeB, target.Current);
        }
    }

    /// <summary>An empty id is a programming error, not a way to name the local server.</summary>
    [Fact]
    public void Beginning_a_scope_for_an_empty_id_is_refused()
    {
        var target = new AsyncLocalAgentTarget();

        Assert.Throws<ArgumentException>(() =>
        {
            target.Begin(Guid.Empty);
        });
    }

    /// <summary>The value flows down into what the opening method awaits.</summary>
    [Fact]
    public async Task The_target_flows_into_an_awaited_callee()
    {
        var target = new AsyncLocalAgentTarget();

        using var scope = target.Begin(NodeA);
        var seen = await ReadAfterYieldAsync(target);

        Assert.Equal(NodeA, seen);
    }

    /// <summary>
    /// A scope opened inside an awaited callee does not reach back up to its caller: the reason a scope
    /// is opened in the method that makes the agent calls.
    /// </summary>
    [Fact]
    public async Task A_scope_opened_in_an_awaited_callee_does_not_reach_its_caller()
    {
        var target = new AsyncLocalAgentTarget();

        await OpenScopeAndLeaveItAsync(target);

        Assert.Equal(FleetServers.LocalId, target.Current);
    }

    /// <summary>Two concurrent flows through one singleton each see their own server.</summary>
    [Fact]
    public async Task Concurrent_flows_keep_their_own_targets()
    {
        var target = new AsyncLocalAgentTarget();

        var results = await Task.WhenAll(
            ReadInOwnScopeAsync(target, NodeA),
            ReadInOwnScopeAsync(target, NodeB));

        Assert.Equal(new[] { NodeA, NodeB }, results);
    }

    /// <summary>Yields, then reads the current target, so the read happens on a continuation.</summary>
    /// <param name="target">The target under test.</param>
    /// <returns>The target as seen after the yield.</returns>
    private static async Task<Guid> ReadAfterYieldAsync(AsyncLocalAgentTarget target)
    {
        await Task.Yield();
        return target.Current;
    }

    /// <summary>Opens a scope and returns without closing it.</summary>
    /// <param name="target">The target under test.</param>
    private static async Task OpenScopeAndLeaveItAsync(AsyncLocalAgentTarget target)
    {
        _ = target.Begin(NodeA);
        await Task.Yield();
    }

    /// <summary>Opens a scope for <paramref name="server"/>, waits, and reports what it sees.</summary>
    /// <param name="target">The target under test.</param>
    /// <param name="server">The server to address.</param>
    /// <returns>The target as seen after the wait.</returns>
    private static async Task<Guid> ReadInOwnScopeAsync(AsyncLocalAgentTarget target, Guid server)
    {
        using var scope = target.Begin(server);
        await Task.Yield();
        await Task.Yield();
        return target.Current;
    }
}
