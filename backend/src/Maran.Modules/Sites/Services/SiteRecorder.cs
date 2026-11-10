using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Interfaces;
using Maran.Modules.Sites.Resources;
using Npgsql;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Writes the row of a site the agent has already provisioned, and removes the vhost again on every
/// path where that row is known not to have been written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which failures after the agent are compensated is drawn by what is KNOWN.</b> A refused insert
/// (<c>DbUpdateException</c>) proves nothing was committed, so the vhost is this request's alone and goes
/// (<see cref="SiteVhostCompensator"/>, which also argues why a loser is compensated, why a pool is never
/// retired and what the compensation cannot close). Anything else the gate throws — the commit failing,
/// the connection dropping around it, the request being cancelled — leaves open whether the row was
/// written, and deleting the vhost of a site whose row did commit is the reverse disagreement, a site the
/// panel shows that does not answer. So it is rethrown untouched, and a vhost with no row can survive it;
/// a process killed between the agent and the insert leaves one too. Both surface as a server failure,
/// never as a typed answer about the request. Once a compensation has been decided on, it runs WITHOUT
/// the request's cancellation: a caller who hangs up after the host work is done must not abandon the
/// removal the hang-up made necessary, and every agent call is already bounded by the agent client's own
/// operation timeout.
/// </para>
/// <para>
/// The slot gate runs after the agent has provisioned the site, so a request refused there leaves a vhost
/// nothing owns; that is why the creation's pre-checks stay, and why this service pays for the loser's
/// vhost on the host.
/// </para>
/// </remarks>
public sealed class SiteRecorder
{
    /// <summary>The atomic claim on the account's last free slot; see <see cref="ISiteSlotGate"/>.</summary>
    private readonly ISiteSlotGate _slotGate;

    /// <summary>Removes the vhost the creation made when its row was not written.</summary>
    private readonly SiteVhostCompensator _compensator;

    /// <summary>Creates the recorder.</summary>
    /// <param name="slotGate">Takes the account's last free slot atomically when the row is written.</param>
    /// <param name="compensator">Removes the vhost again on every path where the row is known not to be written.</param>
    public SiteRecorder(ISiteSlotGate slotGate, SiteVhostCompensator compensator)
    {
        _slotGate = slotGate;
        _compensator = compensator;
    }

    /// <summary>Writes the row for a site the agent has already provisioned, removing its vhost if it cannot.</summary>
    /// <param name="site">The row to write, carrying its hostname claims.</param>
    /// <param name="accountUsername">The owning account's system user name, which addresses the compensating delete.</param>
    /// <param name="allowance">How many sites the account's plan allows, re-checked inside the gate.</param>
    /// <param name="webServer">The web server the vhost was written for, which addresses the compensating delete.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the typed failure the customer is answered with — not yet journalled.</returns>
    /// <exception cref="DbUpdateException">Rethrown, after the vhost is removed, for any failure but a taken hostname.</exception>
    public async Task<Result> RecordAsync(
        Site site,
        string accountUsername,
        int allowance,
        SiteWebServer webServer,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await _slotGate.TryTakeAsync(site, allowance, cancellationToken))
            {
                return Result.Ok();
            }
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The ONE narrowing, and the answer turns on it. SqlState 23505 means another creation
            // committed a claim on one of this site's hostnames — or its domain — first; nothing else
            // does. A DbUpdateException is also the one throw from the gate that proves nothing was
            // committed (ISiteSlotGate), so the vhost this request made is owned by no row of its own
            // and goes, unless the winner's row owns that very file.
            await _compensator.CompensateUnlessOwnedAsync(accountUsername, site.Domain, webServer);

            return Result.Fail(Error.Of(nameof(ErrorMessages.SiteDomainTaken), ErrorType.Conflict));
        }
        catch (DbUpdateException)
        {
            // Any other refused insert says nothing about the request and is rethrown — reporting a
            // dropped connection as "this domain is taken" would tell the customer to change a name
            // that was never the problem. Nothing was committed here either, so the vhost goes first,
            // and unconditionally: asking whether another site owns the file is a read against the
            // database that has just failed, and a same-domain winner would have surfaced as a 23505.
            await _compensator.CompensateAsync(accountUsername, site.Domain, webServer);
            throw;
        }

        // The gate refused, which means another creation committed the account's last slot while this
        // one was on the host. Nothing owns the vhost this request made, so it goes, and the refusal
        // says the plan filled up rather than that the domain was taken — the domain is not the
        // problem and deleting a site the customer no longer needs is what unblocks them.
        await _compensator.CompensateUnlessOwnedAsync(accountUsername, site.Domain, webServer);

        return Result.Fail(Error.Of(nameof(ErrorMessages.SiteLimitReachedConcurrently), ErrorType.Conflict));
    }
}
