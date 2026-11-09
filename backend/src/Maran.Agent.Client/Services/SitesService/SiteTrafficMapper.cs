using System.Globalization;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.SitesService;

/// <summary>
/// Reads the agent's traffic summary into the panel's own mirror types. It sits beside the client
/// because the generated wire types may not leave this project.
/// </summary>
/// <remarks>
/// It restates and refuses; it decides nothing. An absent cursor, an unreadable date and an unset
/// message are all an answer the agent was not supposed to give, and are returned as
/// <c>AgentInvalidResponse</c> here once, so no caller reads a wire default as a fact. The
/// <c>gap</c> and <c>caught_up</c> flags are read here and nowhere else.
/// </remarks>
internal static class SiteTrafficMapper
{
    /// <summary>The format the agent writes a day in.</summary>
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>Reads the success branch of a summarise response.</summary>
    /// <param name="ok">The wire message.</param>
    /// <returns>The reading, or <c>AgentInvalidResponse</c> when the message cannot be read faithfully.</returns>
    public static Result<SiteTrafficReading> Read(SummariseSiteTrafficOk ok)
    {
        if (ok.Cursor is null)
        {
            return Invalid();
        }

        var days = new List<SiteTrafficDayReading>(ok.Days.Count);
        foreach (var day in ok.Days)
        {
            if (!DateOnly.TryParseExact(day.Date, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return Invalid();
            }

            days.Add(new SiteTrafficDayReading(
                date,
                day.Requests,
                day.BodyBytes,
                day.Statuses.Select(status => { return new SiteTrafficStatus(status.Code, status.Requests); }).ToList(),
                day.Paths.Select(path => { return new SiteTrafficPath(path.Path, path.Requests); }).ToList(),
                day.OtherPathRequests));
        }

        return Result<SiteTrafficReading>.Ok(new SiteTrafficReading(
            days,
            new SiteTrafficCursor(ok.Cursor.FileId, ok.Cursor.Offset),
            ok.CaughtUp,
            ok.Gap,
            ok.UnparsedLines));
    }

    /// <summary>Writes the panel's cursor as the wire message, or leaves it absent for a first read.</summary>
    /// <param name="cursor">The stored cursor, or null when the site has never been read.</param>
    /// <returns>The wire cursor, or null.</returns>
    public static TrafficCursor? ToWire(SiteTrafficCursor? cursor)
    {
        if (cursor is null)
        {
            return null;
        }

        return new TrafficCursor { FileId = cursor.FileId, Offset = cursor.Offset };
    }

    /// <summary>The failure for a message the mapper cannot read faithfully.</summary>
    /// <returns>A failed result carrying <c>AgentInvalidResponse</c>.</returns>
    private static Result<SiteTrafficReading> Invalid()
    {
        return Result<SiteTrafficReading>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
    }
}
