using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.HealthChecks;

/// <summary>
/// Counts the handshakes a probe asks for and forwards each to another client, so the attempt count
/// is observable while the answer, and any exception, still comes from the client behind it.
/// </summary>
/// <remarks>
/// Put around the REAL <c>AgentSystemClient</c> over a real socket double, this pins the retry policy
/// against what the transport actually reports, which a fake that decides its own failure cannot.
/// </remarks>
public sealed class CountingAgentSystemClient : IAgentSystemClient
{
    /// <summary>The client every handshake is forwarded to.</summary>
    private readonly IAgentSystemClient _inner;

    /// <summary>How many times the handshake was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>Counts handshakes forwarded to <paramref name="inner"/>.</summary>
    /// <param name="inner">The client that answers.</param>
    public CountingAgentSystemClient(IAgentSystemClient inner)
    {
        _inner = inner;
    }

    /// <inheritdoc/>
    public async Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        Calls++;
        return await _inner.GetInfoAsync(ct);
    }
}
