using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.HealthChecks;

/// <summary>
/// An agent system client that counts the handshakes it is asked for and refuses the first few the way
/// the real client reports an unreachable agent, so the probe pipeline's retry policy is observable
/// from outside.
/// </summary>
/// <remarks>
/// Separate from <see cref="StubAgentSystemClient"/> rather than another constructor on it: that
/// stub answers the same way every time by design — it is what a test uses when the answer is the
/// subject — and a client whose answer CHANGES between attempts is a different fixture, needed only
/// where the number of attempts is the subject.
///
/// The refusal is a failed <see cref="Result{T}"/> carrying <see cref="AgentTransportFailure.Unavailable"/>,
/// because that is what the real client answers: it maps the transport's exception itself and never lets
/// one out of the handshake. A fake that threw a hand-made socket exception would exercise a path
/// production does not have. <see cref="CountingAgentSystemClient"/> counts over the real transport.
/// </remarks>
public sealed class RecordingAgentSystemClient : IAgentSystemClient
{
    /// <summary>How many handshakes are refused as unavailable before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>How many times the handshake was entered.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            return await Task.FromResult(Result<AgentInfoDto>.Fail(AgentTransportFailure.Unavailable()));
        }

        return await Task.FromResult(
            Result<AgentInfoDto>.Ok(new AgentInfoDto(
                "1.0.0",
                "debian",
                "debian",
                AgentProtoVersionWindow.Maximum,
                "/var/backups/maran")));
    }
}
