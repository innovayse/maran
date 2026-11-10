using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Persistence;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Answers, server-wide, which sites already claim a set of hostnames — the one read the creation
/// pre-check and the compensating delete both ask.
/// </summary>
/// <remarks>
/// Deliberately ignores the tenant filter: a hostname is claimed once across the whole server, so
/// a name already served for ANOTHER account is still taken, and the vhost file a compensating
/// delete would remove may be another account's. With the filter on, the conflicting row would be
/// hidden — the pre-check would pass a name another tenant holds, and the compensation would
/// delete another tenant's vhost. The one read around site creation that needs the bypass, so both
/// questions are asked through it rather than each carrying a bypass of its own.
/// </remarks>
public sealed class SiteHostnameClaims
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    public SiteHostnameClaims(SitesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Reads, across every account, the primary domain of each site claiming any of <paramref name="names"/>.</summary>
    /// <param name="names">Lower-cased hostnames, as <see cref="SiteHostname.Name"/> stores them.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One primary domain per claimed name; empty when none of the names is claimed.</returns>
    public async Task<IReadOnlyList<string>> ClaimingSiteDomainsAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // a hostname is claimed server-wide; scoping this read would let one account take another's domain
        return await _dbContext.SiteHostnames
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(hostname => names.Contains(hostname.Name))
            .Select(hostname => hostname.Site.Domain)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
    }

    /// <summary>Whether any site on the server, of any account, already listens on a loopback port.</summary>
    /// <param name="port">The application port a Node site asks for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>True when a committed site already holds the port.</returns>
    /// <remarks>
    /// Unscoped for the reason <see cref="ClaimingSiteDomainsAsync"/> is: the port is on the host's one
    /// loopback interface, which every account shares. Unlike a hostname this is checked and not keyed,
    /// so two simultaneous creations may both pass it; the second application then fails to bind and its
    /// unit reports it, which is why the check is advice to the customer and not the host's defence.
    /// </remarks>
    public async Task<bool> IsAppPortTakenAsync(int port, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // a loopback port is shared by every account; scoping this read would let two accounts claim one
        return await _dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(site => site.AppPort == port, cancellationToken);
#pragma warning restore RS0030
    }
}
