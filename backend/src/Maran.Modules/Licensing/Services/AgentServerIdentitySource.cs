using Maran.Agent.Client.Interfaces;
using Maran.Modules.Licensing.Domain.Interfaces;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Reads this host's machine-id through the agent, turning every failure into
/// <see langword="null"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type's whole job is to not throw.</b> It is the boundary between an RPC, which can fail
/// in every way a network and a daemon can fail, and a verifier that must return a status for any
/// input whatsoever. Everything below collapses to "the machine-id, or null", and the binding
/// policy decides what null means — refusal, which is why this class must never invent a value to
/// paper over a failure.
/// </para>
/// <para>
/// <b>Nothing here logs the value.</b> A machine-id identifies one machine for as long as it
/// exists (rules/security.md item 8). Failures are reported by the agent client's own translator,
/// which logs the error rather than the identity.
/// </para>
/// </remarks>
public sealed class AgentServerIdentitySource : IServerIdentitySource
{
    /// <summary>The monitor client that can ask the agent.</summary>
    private readonly IAgentMonitorClient _monitorClient;

    /// <summary>Creates the source.</summary>
    /// <param name="monitorClient">The monitor client that can ask the agent.</param>
    public AgentServerIdentitySource(IAgentMonitorClient monitorClient)
    {
        _monitorClient = monitorClient;
    }

    /// <inheritdoc/>
    public async Task<string?> TryReadMachineIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _monitorClient.GetServerFingerprintAsync(cancellationToken);

            return result.IsSuccess ? result.Value.MachineId : null;
        }
        catch (Exception)
        {
            // Deliberately every exception, and deliberately silent about the cause here: the agent
            // client has already logged whatever it saw. What this method owes its caller is a
            // value rather than a throw, because the caller is the one method in this module that
            // must return a status for any input at all.
            return null;
        }
    }
}
