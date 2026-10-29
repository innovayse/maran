using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.HealthChecks;

/// <summary>
/// An agent system client whose handshake blocks the calling thread, synchronously, until a test
/// releases it. It models agent I/O that does its work before its first await, which is exactly what a
/// hosted service must not do on the host's startup path.
/// </summary>
public sealed class BlockingAgentSystemClient : IAgentSystemClient, IDisposable
{
    /// <summary>Set by the test to let the blocked handshake finish.</summary>
    private readonly ManualResetEventSlim _release = new(initialState: false);

    /// <summary>Set once a handshake is inside <see cref="GetInfoAsync"/>.</summary>
    private readonly ManualResetEventSlim _entered = new(initialState: false);

    /// <summary>True once a handshake has been entered.</summary>
    public bool Entered
    {
        get
        {
            return _entered.IsSet;
        }
    }

    /// <summary>Releases the handshake and disposes the two events.</summary>
    public void Dispose()
    {
        _release.Set();
        _release.Dispose();
        _entered.Dispose();
    }

    /// <summary>Lets every blocked handshake, present and future, finish.</summary>
    public void Release()
    {
        _release.Set();
    }

    /// <inheritdoc/>
    public Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        _entered.Set();
        _release.Wait(TimeSpan.FromSeconds(30), ct);

        return Task.FromResult(Result<AgentInfoDto>.Ok(new AgentInfoDto(
            "1.0.0",
            "debian",
            "debian",
            AgentProtoVersionWindow.Maximum,
            "/var/backups/maran")));
    }
}
