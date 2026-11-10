using Maran.Modules.ServerMigration.Interfaces;
using Maran.Modules.ServerMigration.Models;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Fetches each migrated site from this panel's own web server and states what answered (issue #197, phase 2).
/// </summary>
/// <remarks>
/// The target is the probe's constant, never the source or the inventory (see <see cref="ISiteProbe"/>). A site that
/// does not answer is a row in the report with no status and <c>Serves = false</c>, not a failure of the cut-over: the
/// administrator reads the report and decides, because a site can fail to serve for reasons that are not the
/// migration's (a certificate still to be issued).
/// </remarks>
public sealed class MigrationVerifier
{
    /// <summary>The probe.</summary>
    private readonly ISiteProbe _probe;

    /// <summary>Creates the verifier.</summary>
    /// <param name="probe">The probe.</param>
    public MigrationVerifier(ISiteProbe probe)
    {
        _probe = probe;
    }

    /// <summary>Probes each domain in turn.</summary>
    /// <param name="domains">The sites' primary host names.</param>
    /// <param name="cancellationToken">Cancels the probes.</param>
    /// <returns>One row per domain, in the order given.</returns>
    public async Task<IReadOnlyList<MigrationSiteReport>> ProbeAsync(
        IReadOnlyList<string> domains,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domains);

        var rows = new List<MigrationSiteReport>();
        foreach (var domain in domains)
        {
            var result = await _probe.ProbeAsync(domain, cancellationToken);
            rows.Add(new MigrationSiteReport(
                domain,
                result.StatusCode,
                result.Title,
                result.StatusCode is >= 200 and < 400));
        }

        return rows;
    }
}
