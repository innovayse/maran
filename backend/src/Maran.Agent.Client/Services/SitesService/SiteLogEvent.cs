namespace Maran.Agent.Client.Services.SitesService;

/// <summary>
/// One event from a tailed site log: a line, an ending the agent or the caller named, or a failure.
/// Exactly one of three cases, each its own type, so a line never carries an ending's fields and an
/// ending never carries a placeholder line.
/// </summary>
/// <remarks>
/// <para>
/// The distinction between the endings is the point of the type. A log the operator is watching that
/// simply stops producing lines looks identical, at the call site, whether the agent closed it
/// because nothing was written, dropped it because the panel stopped reading, failed the operation,
/// or reached its natural end. Collapsing those into "the enumeration finished" is a silent
/// truncation: the operator keeps watching a pane that will never update again. Each ending therefore
/// arrives as its own final event.
/// </para>
/// <para>
/// The constructor is private to this assembly, so <see cref="SiteLogLine"/>,
/// <see cref="SiteLogEnded"/> and <see cref="SiteLogFailed"/> are the whole set.
/// </para>
/// </remarks>
public abstract record SiteLogEvent
{
    /// <summary>Closes the hierarchy to the three events this assembly declares.</summary>
    private protected SiteLogEvent()
    {
    }
}
