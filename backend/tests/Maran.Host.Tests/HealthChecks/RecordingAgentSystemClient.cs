using System.Net.Sockets;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.HealthChecks;

/// <summary>
/// An agent system client that counts the handshakes it is asked for and refuses the first few with
/// a transport error, so the probe pipeline's retry policy is observable from outside.
/// </summary>
/// <remarks>
/// Separate from <see cref="StubAgentSystemClient"/> rather than another constructor on it: that
/// stub answers the same way every time by design — it is what a test uses when the answer is the
/// subject — and a client whose answer CHANGES between attempts is a different fixture, needed only
/// where the number of attempts is the subject.
/// </remarks>
public sealed class RecordingAgentSystemClient : IAgentSystemClient
{
    /// <summary>How many handshakes fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>How many times the handshake was entered.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        return await Task.FromResult(
            Result<AgentInfoDto>.Ok(new AgentInfoDto("1.0.0", "debian", "debian", 1, "/var/backups/maran")));
    }
}
