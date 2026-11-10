using Maran.Agent.Client.Services.DnsService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the agent's DNS service: the authoritative name server this host runs and the
/// zones it serves. Panel-side vocabulary only — no generated type crosses this seam.
/// </summary>
/// <remarks>
/// <para>
/// Every failure is a typed <see cref="Error"/> carrying a code and nothing else; the agent's own text is
/// logged at the boundary that receives it (rules/csharp.md). No member accepts a configuration fragment,
/// a zone file, a path or a command: a zone travels as validated fields and the agent renders it.
/// </para>
/// <para>
/// <see cref="PutZoneAsync"/> carries the COMPLETE record set of the zone and replaces what the daemon
/// served, so repeating it converges. The serial in it is computed by the panel and never derived by the
/// agent.
/// </para>
/// </remarks>
public interface IAgentDnsClient
{
    /// <summary>Configures and starts the name server. The firewall is not touched.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The daemon's state after the change, or the agent's typed failure.</returns>
    Task<Result<DnsDaemonStatusDto>> EnableAsync(CancellationToken cancellationToken);

    /// <summary>Stops the name server. The firewall is not touched. Zone files stay where they are.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The daemon's state after the change, or the agent's typed failure.</returns>
    Task<Result<DnsDaemonStatusDto>> DisableAsync(CancellationToken cancellationToken);

    /// <summary>Reads what the name server is actually doing, never what was last asked of it.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The observed state, or the agent's typed failure.</returns>
    Task<Result<DnsDaemonStatusDto>> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Renders, validates and serves one zone from its complete record set.</summary>
    /// <param name="zone">The zone and every record in it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The serial the daemon now serves, or the agent's typed failure.</returns>
    Task<Result<DnsZoneServedDto>> PutZoneAsync(DnsZoneDefinition zone, CancellationToken cancellationToken);

    /// <summary>Stops serving a zone and removes its file.</summary>
    /// <param name="zoneName">The zone's apex name.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success, or the agent's typed failure (a zone it does not serve is <c>NotFound</c>).</returns>
    Task<Result> DeleteZoneAsync(string zoneName, CancellationToken cancellationToken);

    /// <summary>Reads what the daemon serves for one zone.</summary>
    /// <param name="zoneName">The zone's apex name.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The served serial, or the agent's typed failure.</returns>
    Task<Result<DnsZoneServedDto>> GetZoneAsync(string zoneName, CancellationToken cancellationToken);

    /// <summary>Lists every zone the daemon serves.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The apex names of the served zones, or the agent's typed failure.</returns>
    Task<Result<IReadOnlyList<string>>> ListZonesAsync(CancellationToken cancellationToken);
}
