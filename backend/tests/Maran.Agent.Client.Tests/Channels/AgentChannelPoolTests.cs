using System.Security.Cryptography.X509Certificates;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Models;
using Maran.Agent.Client.Targets;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.SharedKernel.Constants;

namespace Maran.Agent.Client.Tests.Channels;

/// <summary>How the pool shares, replaces, refuses and disposes the per-server channels.</summary>
public sealed class AgentChannelPoolTests : IDisposable
{
    /// <summary>A managed node's id.</summary>
    private static readonly Guid Node = new("33333333-3333-3333-3333-333333333333");

    /// <summary>The certificates created for this test, disposed with it.</summary>
    private readonly List<IDisposable> _owned = [];

    /// <summary>Releases the certificates.</summary>
    public void Dispose()
    {
        foreach (var item in _owned)
        {
            item.Dispose();
        }
    }

    /// <summary>The local server's channel is built over the unix socket and shared by every lookup.</summary>
    [Fact]
    public void The_local_channel_is_shared_across_lookups()
    {
        var (pool, _, _) = Create();
        using var owned = pool;

        Assert.Same(pool.ForCurrent(), pool.ForServer(FleetServers.LocalId));
    }

    /// <summary>With no scope open <see cref="AgentChannelPool.ForCurrent"/> addresses the local server, as before.</summary>
    [Fact]
    public void Without_a_scope_the_current_channel_is_the_local_one()
    {
        var (pool, _, _) = Create();
        using var owned = pool;

        Assert.Same(pool.ForServer(FleetServers.LocalId), pool.ForCurrent());
    }

    /// <summary>An open scope makes the current channel the chosen server's, and it is not the local one.</summary>
    [Fact]
    public void A_scope_selects_the_channel_of_its_server()
    {
        var (pool, source, target) = Create();
        using var owned = pool;
        source.Set(Node, NodeEndpoint());

        using (target.Begin(Node))
        {
            Assert.Same(pool.ForServer(Node), pool.ForCurrent());
            Assert.NotSame(pool.ForServer(FleetServers.LocalId), pool.ForCurrent());
        }
    }

    /// <summary>The same endpoint yields the same channel on every lookup.</summary>
    [Fact]
    public void A_server_keeps_one_channel_while_its_endpoint_is_unchanged()
    {
        var (pool, source, _) = Create();
        using var owned = pool;
        source.Set(Node, NodeEndpoint());

        Assert.Same(pool.ForServer(Node), pool.ForServer(Node));
    }

    /// <summary>An endpoint that changed (a re-approved node, a new pin) gets a new channel.</summary>
    [Fact]
    public void A_changed_endpoint_replaces_the_channel()
    {
        var (pool, source, _) = Create();
        using var owned = pool;
        var identity = Own(TestCertificates.CreateValid("panel"));
        source.Set(Node, new MutualTlsAgentEndpoint("node.example", 8444, identity, new string('a', 64)));
        var before = pool.ForServer(Node);

        source.Set(Node, new MutualTlsAgentEndpoint("node.example", 8444, identity, new string('b', 64)));
        var after = pool.ForServer(Node);

        Assert.NotSame(before, after);
    }

    /// <summary>A server that is not approved is never dialled: the lookup refuses with the source's code.</summary>
    [Fact]
    public void A_refused_server_is_refused_with_its_code()
    {
        var (pool, source, _) = Create();
        using var owned = pool;
        source.Refuse(Node, "AgentServerNotApproved");

        var refusal = Assert.Throws<AgentEndpointRefusedException>(() =>
        {
            pool.ForServer(Node);
        });

        Assert.Equal("AgentServerNotApproved", refusal.Error.Code);
    }

    /// <summary>An unknown server is refused as unknown and is never answered with the local channel.</summary>
    [Fact]
    public void An_unknown_server_is_refused_and_never_falls_back_to_the_local_channel()
    {
        var (pool, _, _) = Create();
        using var owned = pool;

        var refusal = Assert.Throws<AgentEndpointRefusedException>(() =>
        {
            pool.ForServer(Guid.NewGuid());
        });

        Assert.Equal("AgentServerUnknown", refusal.Error.Code);
    }

    /// <summary>A server that stopped resolving is refused on the next lookup, not served from the cache.</summary>
    [Fact]
    public void A_server_that_stops_resolving_is_not_served_from_the_cache()
    {
        var (pool, source, _) = Create();
        using var owned = pool;
        source.Set(Node, NodeEndpoint());
        _ = pool.ForServer(Node);

        source.Refuse(Node, "AgentServerDisabled");

        Assert.Throws<AgentEndpointRefusedException>(() =>
        {
            pool.ForServer(Node);
        });
    }

    /// <summary>After the pool is disposed, a lookup fails instead of opening a new connection.</summary>
    [Fact]
    public void A_disposed_pool_refuses_lookups()
    {
        var (pool, _, _) = Create();
        pool.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            pool.ForServer(FleetServers.LocalId);
        });
    }

    /// <summary>Builds a pool over a scripted source that knows the local server.</summary>
    /// <returns>The pool, its source and its target.</returns>
    private static (AgentChannelPool Pool, ScriptedEndpointSource Source, AsyncLocalAgentTarget Target) Create()
    {
        var source = new ScriptedEndpointSource();
        source.Set(FleetServers.LocalId, new UnixSocketAgentEndpoint("/nonexistent/agent.sock"));
        var target = new AsyncLocalAgentTarget();
        var pool = new AgentChannelPool(source, target, new FixedClock(TestCertificates.Now));
        return (pool, source, target);
    }

    /// <summary>A mutual-TLS endpoint with a fresh panel identity and a fixed pin.</summary>
    /// <returns>The endpoint.</returns>
    private MutualTlsAgentEndpoint NodeEndpoint()
    {
        var identity = Own(TestCertificates.CreateValid("panel"));
        return new MutualTlsAgentEndpoint("node.example", 8444, identity, new string('a', 64));
    }

    /// <summary>Registers a certificate for disposal with the test.</summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The same certificate.</returns>
    private X509Certificate2 Own(X509Certificate2 certificate)
    {
        _owned.Add(certificate);
        return certificate;
    }
}
