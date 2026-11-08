using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Models;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Tests.TestSupport;

/// <summary>An <see cref="IAgentEndpointSource"/> whose answers a test sets, standing in for the registry of servers.</summary>
internal sealed class ScriptedEndpointSource : IAgentEndpointSource
{
    /// <summary>What each server currently resolves to; a missing id is unknown.</summary>
    private readonly Dictionary<Guid, Result<AgentEndpoint>> _answers = [];

    /// <summary>How many times <see cref="Resolve"/> has been asked.</summary>
    public int Lookups { get; private set; }

    /// <summary>Makes a server resolve to <paramref name="endpoint"/>.</summary>
    /// <param name="serverId">The server.</param>
    /// <param name="endpoint">Where its agent is.</param>
    public void Set(Guid serverId, AgentEndpoint endpoint)
    {
        _answers[serverId] = Result<AgentEndpoint>.Ok(endpoint);
    }

    /// <summary>Makes a server refuse with <paramref name="code"/>.</summary>
    /// <param name="serverId">The server.</param>
    /// <param name="code">The refusal code.</param>
    public void Refuse(Guid serverId, string code)
    {
        _answers[serverId] = Result<AgentEndpoint>.Fail(Error.Of(code, ErrorType.Conflict));
    }

    /// <inheritdoc/>
    public Result<AgentEndpoint> Resolve(Guid serverId)
    {
        Lookups++;
        return _answers.TryGetValue(serverId, out var answer)
            ? answer
            : Result<AgentEndpoint>.Fail(Error.Of("AgentServerUnknown", ErrorType.NotFound));
    }
}
