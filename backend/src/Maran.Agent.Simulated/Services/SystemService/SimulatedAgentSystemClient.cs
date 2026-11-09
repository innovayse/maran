using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;

namespace Maran.Agent.Simulated.Services.SystemService;

/// <summary>
/// The handshake of a host that is not there: answers with an identity and the newest contract
/// revision the panel accepts, so the panel's connected check passes by value, not by bypass.
/// </summary>
/// <remarks>
/// The proto version is <see cref="AgentProtoVersionWindow.Maximum"/>, the same constant the real
/// handshake is judged against, so the answer cannot fall out of the window when the window moves.
/// </remarks>
public sealed class SimulatedAgentSystemClient : IAgentSystemClient
{
    /// <summary>The version string the simulated agent reports.</summary>
    public const string SimulatedVersion = "simulated";

    /// <summary>The distribution id the simulated host reports.</summary>
    public const string DistroId = "ubuntu";

    /// <summary>The directory the simulated host reports as its local backup root.</summary>
    public const string BackupRoot = "/var/backups/maran";

    /// <inheritdoc />
    public Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var info = new AgentInfoDto(
            SimulatedVersion,
            DistroId,
            AgentDistroFamily.Debian,
            AgentProtoVersionWindow.Maximum,
            BackupRoot);

        return Task.FromResult(Result<AgentInfoDto>.Ok(info));
    }
}
