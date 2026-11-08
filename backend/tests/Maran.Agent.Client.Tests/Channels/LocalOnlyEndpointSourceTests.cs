using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Models;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Tests.Channels;

/// <summary>The endpoint source of a panel with no managed servers.</summary>
public sealed class LocalOnlyEndpointSourceTests
{
    /// <summary>The local server resolves to the agent's unix socket at the configured path.</summary>
    [Fact]
    public void The_local_server_resolves_to_the_configured_unix_socket()
    {
        var source = new LocalOnlyEndpointSource("/run/maran/agent.sock");

        var resolved = source.Resolve(FleetServers.LocalId);

        Assert.True(resolved.IsSuccess);
        Assert.Equal(new UnixSocketAgentEndpoint("/run/maran/agent.sock"), resolved.Value);
    }

    /// <summary>Any other id is unknown, never quietly answered with the local socket.</summary>
    [Fact]
    public void Any_other_server_is_unknown_and_never_falls_back_to_the_local_socket()
    {
        var source = new LocalOnlyEndpointSource("/run/maran/agent.sock");

        var resolved = source.Resolve(Guid.NewGuid());

        Assert.False(resolved.IsSuccess);
        Assert.Equal("AgentServerUnknown", resolved.Error!.Code);
        Assert.Equal(ErrorType.NotFound, resolved.Error.Type);
    }
}
