using Maran.Modules.Licensing.Domain.Interfaces;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Reads the id of this server and builds the link to the claim page: the one place the licence screen, the
/// install answer and <c>maran status</c> get them, so the three can never name different servers.
/// </summary>
/// <remarks>
/// <para>
/// <b>One field, one meaning: the id is the very value the licence is judged against.</b> It is read through
/// <see cref="IServerIdentitySource"/>, the same seam <see cref="LicenceVerifier"/>, <see cref="LicenceCoverageSource"/>
/// and <see cref="LeaseRenewer"/> use, and is returned as read, so what an administrator copies is what a bound
/// licence has to name. Nothing is derived, hashed or shortened for display.
/// </para>
/// <para>
/// <b>Why it may be shown.</b> The machine id is not a secret on its own host and the administrator who reads it
/// is the one who can already read <c>/etc/machine-id</c>; the audit journal and the logs still never carry it
/// (<see cref="IServerIdentitySource"/>), and this type writes to neither. The threat note's fifth amendment
/// (<c>docs/superpowers/notes/2026-11-07-licensing-lease-threat-note.md</c>) records the decision.
/// </para>
/// <para>
/// <b>Never throws.</b> The identity source answers null for every failure, and a configured address that is
/// not a usable origin yields no link.
/// </para>
/// </remarks>
public sealed class LicenceServerFactsSource
{
    /// <summary>Reads this host's machine id.</summary>
    private readonly IServerIdentitySource _serverIdentity;

    /// <summary>Where the claim page is.</summary>
    private readonly IOptions<LicenceClaimOptions> _options;

    /// <summary>Creates the source.</summary>
    /// <param name="serverIdentity">Reads this host's machine id.</param>
    /// <param name="options">Where the claim page is.</param>
    public LicenceServerFactsSource(IServerIdentitySource serverIdentity, IOptions<LicenceClaimOptions> options)
    {
        _serverIdentity = serverIdentity;
        _options = options;
    }

    /// <summary>Reads the server id and builds the claim link.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The id and the link; either is null when it cannot be given.</returns>
    public async Task<LicenceServerFacts> ReadAsync(CancellationToken cancellationToken)
    {
        var serverId = await _serverIdentity.TryReadMachineIdAsync(cancellationToken);

        return new LicenceServerFacts(
            serverId,
            LicenceClaimLinkPolicy.Build(_options.Value.ClaimPageBaseUrl, serverId));
    }
}
