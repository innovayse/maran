using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Persistence;
using AgentTrafficCursor = Maran.Agent.Client.Services.SitesService.SiteTrafficCursor;
using AgentTrafficDayReading = Maran.Agent.Client.Services.SitesService.SiteTrafficDayReading;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Reads one site's access log since the last pass and adds what it counted to the site's day rows, moving
/// the cursor in the same transaction (issue #152).
/// </summary>
/// <remarks>
/// <para>
/// <b>Counts and cursor are one commit.</b> Both are staged on the context and written by a single
/// <c>SaveChanges</c>, which is one transaction. If the agent call succeeded and the write fails, the old
/// cursor is still stored and the next pass reads the same lines again, so nothing is counted twice and
/// nothing is skipped.
/// </para>
/// <para>
/// <b>The answer is paired with the site that was asked about.</b> The agent's response carries no site
/// or account identifier and none is read: every row written here belongs to the site passed in, so a
/// wrong or hostile answer can put wrong figures on that site and on no other tenant's.
/// </para>
/// <para>
/// <b>It reads across accounts on purpose.</b> There is no principal in an unattended pass, so the cursor
/// and day lookups lift the tenant filter, each saying why. The site itself arrives already loaded by the
/// caller, who found it for the same reason.
/// </para>
/// <para>
/// Not safe to run twice at once on one site; <see cref="SiteLogMaintenanceGate"/> serialises its callers.
/// </para>
/// </remarks>
public sealed class SiteTrafficIngestor
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The agent, which owns the log files.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>The injected time source; never the ambient clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the ingestor.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="agent">The agent client that reads the logs.</param>
    /// <param name="clock">The time source the cursor and gap instants come from.</param>
    public SiteTrafficIngestor(SitesDbContext dbContext, IAgentSitesClient agent, IClock clock)
    {
        _dbContext = dbContext;
        _agent = agent;
        _clock = clock;
    }

    /// <summary>Reads one site's log from its cursor and stores the counts and the new cursor.</summary>
    /// <param name="site">The site to read, loaded and tracked by the caller.</param>
    /// <param name="accountUsername">The owning account's system user name, which addresses the agent call.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>Success, or the agent's typed failure; in that case nothing was written.</returns>
    public async Task<Result> IngestAsync(Site site, string accountUsername, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // unattended pass: no principal, so the filter would show no cursor and every site would re-read from the start
        var cursor = await _dbContext.SiteTrafficCursors
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(stored => stored.SiteId == site.Id, cancellationToken);
#pragma warning restore RS0030

        var reading = await _agent.SummariseTrafficAsync(
            accountUsername,
            site.Domain,
            cursor is null ? null : new AgentTrafficCursor(cursor.FileId, (ulong)cursor.Offset),
            cancellationToken);
        if (!reading.IsSuccess)
        {
            return Result.Fail(reading.Error!);
        }

        var now = _clock.UtcNow;
        var value = reading.Value;

        await AbsorbDaysAsync(site, value.Days, cancellationToken);

        var offset = Clamp(value.Cursor.Offset);
        if (cursor is null)
        {
            cursor = new SiteTrafficCursor(site, value.Cursor.FileId, offset, now);
            _dbContext.SiteTrafficCursors.Add(cursor);
        }
        else
        {
            cursor.Advance(value.Cursor.FileId, offset, now);
        }

        if (value.Gap)
        {
            // The lines between the old cursor and the file the agent found are gone. The panel does not
            // guess them back; it remembers when it saw the loss so the summary can say it may be short.
            cursor.RecordGap(now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }

    /// <summary>Adds each day of a reading to the site's stored row for that day, creating rows that do not exist.</summary>
    /// <param name="site">The site the days belong to.</param>
    /// <param name="days">The days the agent counted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    private async Task AbsorbDaysAsync(
        Site site,
        IReadOnlyList<AgentTrafficDayReading> days,
        CancellationToken cancellationToken)
    {
        if (days.Count == 0)
        {
            return;
        }

        var dates = days.Select(day => { return day.Date; }).Distinct().ToList();

#pragma warning disable RS0030 // unattended pass: no principal, so the filter would hide the stored days and each read would insert a duplicate key
        var stored = (await _dbContext.SiteTrafficDays
            .IgnoreQueryFilters()
            .Where(day => day.SiteId == site.Id && dates.Contains(day.Date))
            .ToListAsync(cancellationToken))
            .ToDictionary(day => { return day.Date; });
#pragma warning restore RS0030

        foreach (var reading in days)
        {
            if (!stored.TryGetValue(reading.Date, out var day))
            {
                day = new SiteTrafficDay(site, reading.Date);
                _dbContext.SiteTrafficDays.Add(day);
                stored[reading.Date] = day;
            }

            day.Absorb(ToDelta(reading));
        }
    }

    /// <summary>Restates one day of the agent's reading as the domain's delta.</summary>
    /// <param name="reading">The agent's counts for the day.</param>
    /// <returns>The same counts as signed numbers a column holds.</returns>
    private static TrafficDelta ToDelta(AgentTrafficDayReading reading)
    {
        return new TrafficDelta(
            Clamp(reading.Requests),
            Clamp(reading.BodyBytes),
            reading.Statuses
                .Select(status => { return new TrafficStatusCount((int)Math.Min(status.Code, int.MaxValue), Clamp(status.Requests)); })
                .ToList(),
            reading.Paths
                .Select(path => { return new TrafficPathCount(path.Path, Clamp(path.Requests)); })
                .ToList(),
            Clamp(reading.OtherPathRequests));
    }

    /// <summary>Narrows an unsigned count to the signed number a column holds, without wrapping.</summary>
    /// <param name="value">The agent's count.</param>
    /// <returns>The count, or the largest signed value when it would not fit.</returns>
    private static long Clamp(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }
}
