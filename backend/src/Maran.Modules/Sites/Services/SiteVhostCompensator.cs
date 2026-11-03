using Maran.Agent.Client.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Removes the vhost a site creation provisioned on the host when the row that defines the site was
/// not written, and withholds the removal when a committed site owns that very file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this loser is compensated, when the Ftp and Sftp losers of the same race are not.</b> There
/// the winner and the loser made the SAME login on the host, so a delete would revoke the winner's
/// credential. A vhost is a file named after the site's PRIMARY domain, so a loser that collided on an
/// alias, or whose domain collided with another site's alias, wrote a file of its own that no row owns,
/// and it goes. The two coincide in exactly one case: both requests named the same primary domain and
/// both wrote the one file. The agent refuses a creation whose vhost is already there, under the same
/// lock, and the creation handler again does not rely on that: if it happens, the file now belongs to
/// the winner's row, deleting it would take the winner's site off the air, so the delete is withheld and
/// reported (<see cref="CompensateUnlessOwnedAsync"/>) — the same conservative direction the pool
/// paragraph below takes. Which of the two renderings the file then holds, this service cannot see.
/// </para>
/// <para>
/// <b>What the compensation cannot close on its own.</b> Between the loser's reload and its
/// compensating delete the loser's vhost is live, and for those seconds it may answer for the winner's
/// name. The database side cannot close that window; the agent's refusal above is what keeps such a
/// vhost off the host in the first place. Taking the claims before the agent is called would close it
/// here too, and was not chosen: a row that exists before its vhost is a row every reader of this
/// module — the site list, suspension, deletion, the certificate directory — would have to learn to
/// skip, and a process killed between the claim and the agent leaves a claim nothing releases, which
/// needs a pending state, a migration and a reconciler of its own.
/// </para>
/// <para>
/// <b>The loser pays for it on the host, which is why the pre-checks stay.</b> The slot gate runs after
/// the agent has provisioned the site, so a request refused there leaves a vhost nothing owns, and it is
/// removed again. The compensating delete retires NO php-fpm pool — it passes the empty version, which
/// the agent documents as "leave every pool alone" — because a pool belongs to an ACCOUNT and a version
/// rather than to one site, and the account's other sites on that version share it. An orphaned pool is
/// wasteful and an operator can see it; a removed shared pool takes another of the customer's sites off
/// the air, so the conservative direction is the only defensible one for a compensation.
/// </para>
/// </remarks>
public sealed class SiteVhostCompensator
{
    /// <summary>Pre-compiled log delegate for a compensation that did not take.</summary>
    /// <remarks>
    /// Source-generated because what it reports is an orphaned vhost on the host that only an operator
    /// can clear, and a message an operator has to find must be searchable and structured rather than
    /// interpolated. It names the site's domain and the agent's own error CODE, never the agent's text
    /// and never a path.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogCompensationFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(SiteVhostCompensator)),
            "Provisioned site {Domain} could not be recorded and could not be removed either "
            + "({AgentErrorCode}); its vhost is now on the host with no row.");

    /// <summary>Pre-compiled log delegate for a compensation withheld because the vhost is another site's.</summary>
    /// <remarks>
    /// An error and not a warning, because what it reports needs an operator: two creations of one
    /// domain both wrote its vhost, the other one's row owns it, and the file may hold THIS request's
    /// rendering — its document root and its aliases — until the owning site's vhost is rendered
    /// again. It names the domain only; never a path and never an account.
    /// </remarks>
    private static readonly Action<ILogger, string, Exception?> LogCompensationWithheld =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, nameof(SiteVhostCompensator)),
            "Provisioned site {Domain} lost its name to a concurrent creation of the same domain after "
            + "both had written its vhost; the vhost was left in place because the winning site's row "
            + "owns it, and it may carry the losing request's rendering until that site is re-rendered.");

    /// <summary>Reads which committed sites claim a hostname, across every account.</summary>
    private readonly SiteHostnameClaims _claims;

    /// <summary>The agent, which owns the vhost that has to be removed.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>Where a failed or withheld compensation is reported, since the customer is told nothing about it.</summary>
    private readonly ILogger<SiteVhostCompensator> _logger;

    /// <summary>Creates the compensator.</summary>
    /// <param name="claims">Reads which committed sites claim a hostname, across every account.</param>
    /// <param name="agent">The agent client that removes the vhost.</param>
    /// <param name="logger">Where a failed or withheld compensation is reported.</param>
    public SiteVhostCompensator(SiteHostnameClaims claims, IAgentSitesClient agent, ILogger<SiteVhostCompensator> logger)
    {
        _claims = claims;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Removes the vhost this request made, unless a committed site with the same domain owns that file.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domain">The creation's primary domain, which addresses the delete.</param>
    /// <remarks>
    /// <para>
    /// Takes no cancellation token: once the removal is decided on it must not be abandoned by a caller
    /// hanging up (see the creation handler's remarks), and the agent client bounds the call itself.
    /// </para>
    /// <para>
    /// The agent names a vhost after the site's primary domain alone, lower-cased, and deletes by that
    /// name without asking which account wrote it. So the question before a compensating delete is
    /// not "did this request write the file" — it did — but "does a committed row now own that same
    /// file". It does exactly when this request's domain is claimed as some site's PRIMARY domain: a
    /// claim held as another site's alias names a different file, and an unclaimed domain names a
    /// file nobody owns. Only the first case withholds the delete.
    /// </para>
    /// </remarks>
    public async Task CompensateUnlessOwnedAsync(string accountUsername, string domain)
    {
        var claimants = await _claims.ClaimingSiteDomainsAsync([domain.ToLowerInvariant()], CancellationToken.None);
        var ownedByAnotherSite = claimants.Any(claimant =>
        {
            return string.Equals(claimant, domain, StringComparison.OrdinalIgnoreCase);
        });
        if (ownedByAnotherSite)
        {
            LogCompensationWithheld(_logger, domain, null);
            return;
        }

        await CompensateAsync(accountUsername, domain);
    }

    /// <summary>Removes a vhost the agent provisioned but no row owns.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domain">The creation's primary domain, which addresses the delete.</param>
    /// <remarks>
    /// Takes no cancellation token, for the reason <see cref="CompensateUnlessOwnedAsync"/> gives.
    /// Best effort, and logged rather than surfaced: the customer is already being told the creation
    /// failed, and a second failure here changes nothing they can act on. The empty PHP version is
    /// deliberate and is argued on the type — it leaves every php-fpm pool standing, because a pool is
    /// shared by the account's other sites on that version and removing one would take them off the
    /// air, while an orphaned pool is merely wasteful.
    /// </remarks>
    public async Task CompensateAsync(string accountUsername, string domain)
    {
        var deleted = await _agent.DeleteAsync(accountUsername, domain, string.Empty, CancellationToken.None);
        if (!deleted.IsSuccess)
        {
            LogCompensationFailed(_logger, domain, deleted.Error!.Code, null);
        }
    }
}
