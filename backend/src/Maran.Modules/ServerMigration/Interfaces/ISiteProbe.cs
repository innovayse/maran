using Maran.Modules.ServerMigration.Models;

namespace Maran.Modules.ServerMigration.Interfaces;

/// <summary>
/// Fetches a migrated site from THIS panel's own web server, for the comparison report (issue #197, phase 2).
/// </summary>
/// <remarks>
/// A seam so the verifier is driven without a web server. The production implementation connects to the loopback
/// address and nothing else: the address is a constant in code and the domain only fills the <c>Host</c> header, so
/// neither the source nor the inventory can aim it anywhere (no SSRF primitive).
/// </remarks>
public interface ISiteProbe
{
    /// <summary>Requests the front page of a site by its host name.</summary>
    /// <param name="domain">A host name that passed the ordinary domain validation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What came back; a web server that did not answer is a result with no status, never an exception.</returns>
    Task<SiteProbeResult> ProbeAsync(string domain, CancellationToken cancellationToken);
}
