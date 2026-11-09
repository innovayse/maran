using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;

namespace Maran.Modules.Sites.Queries.GetSiteTraffic;

/// <summary>Handles <see cref="GetSiteTrafficQuery"/> by reading one site's stored daily figures.</summary>
/// <remarks>
/// <para>
/// <b>It reads PostgreSQL and nothing else.</b> The agent is not asked: the figures were counted by the
/// hourly maintenance pass, so a customer request has nothing to amplify into root work.
/// </para>
/// <para>
/// The site is read through the tenant-filtered context, so another customer's site is not found, and
/// the day rows and the cursor are filtered through that site as well. A site with no rows yet is an
/// ordinary first state and answers <c>hasData: false</c>, not an error.
/// </para>
/// </remarks>
public sealed class GetSiteTrafficQueryHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The injected time source the window is measured against; never the ambient clock.</summary>
    private readonly IClock _clock;

    /// <summary>Names the status classes in the request's culture.</summary>
    private readonly SiteDisplayNames _siteNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="clock">The time source the window is measured against.</param>
    /// <param name="siteNames">Names the status classes for the request's culture.</param>
    public GetSiteTrafficQueryHandler(SitesDbContext dbContext, IClock clock, SiteDisplayNames siteNames)
    {
        _dbContext = dbContext;
        _clock = clock;
        _siteNames = siteNames;
    }

    /// <summary>Returns the site's traffic over the window, or <c>SiteNotFound</c>.</summary>
    /// <param name="query">Which site, and which window.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The figures, or <c>SiteNotFound</c>.</returns>
    public async Task<Result<SiteTrafficDto>> HandleAsync(
        GetSiteTrafficQuery query,
        CancellationToken cancellationToken)
    {
        var siteExists = await _dbContext.Sites
            .AsNoTracking()
            .AnyAsync(site => site.Id == query.SiteId, cancellationToken);
        if (!siteExists)
        {
            return Result<SiteTrafficDto>.Fail(Error.Of(nameof(ErrorMessages.SiteNotFound), ErrorType.NotFound));
        }

        var days = query.Days ?? TrafficHistory.Windows[0];
        var to = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var from = to.AddDays(1 - days);

        var rows = await _dbContext.SiteTrafficDays
            .AsNoTracking()
            .Where(day => day.SiteId == query.SiteId && day.Date >= from && day.Date <= to)
            .OrderBy(day => day.Date)
            .ToListAsync(cancellationToken);

        var cursor = await _dbContext.SiteTrafficCursors
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.SiteId == query.SiteId, cancellationToken);

        // A gap older than the window cannot have shortened what the window shows, so it is not
        // reported: a notice about a week nobody is looking at is noise that hides the one that matters.
        DateOnly? incompleteSince = null;
        if (cursor?.IncompleteSince is { } lostAt)
        {
            var lostDay = DateOnly.FromDateTime(lostAt.UtcDateTime);
            if (lostDay >= from)
            {
                incompleteSince = lostDay;
            }
        }

        return Result<SiteTrafficDto>.Ok(SiteTrafficMapper.ToDto(
            days, from, to, rows, incompleteSince, cursor?.UpdatedAt, _siteNames));
    }
}
