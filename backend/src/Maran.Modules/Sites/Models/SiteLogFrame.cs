namespace Maran.Modules.Sites.Models;

/// <summary>
/// One thing to write to a watching client: either a log line, or the ending of the stream. The
/// two travel as one sequence so the writer consumes a single stream and cannot forget the ending,
/// and as two types so a line never carries an ending's fields and an ending never carries a
/// placeholder line.
/// </summary>
/// <remarks>
/// The constructor is private to this assembly, so <see cref="SiteLogLineFrame"/> and
/// <see cref="SiteLogEndFrame"/> are the whole set. A sequence of frames ends with exactly one
/// <see cref="SiteLogEndFrame"/>.
/// </remarks>
public abstract record SiteLogFrame
{
    /// <summary>Closes the hierarchy to the two frames this module declares.</summary>
    private protected SiteLogFrame()
    {
    }
}
