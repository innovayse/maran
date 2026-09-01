using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Common;

/// <summary>
/// One thing to write to a watching client: either a log line, or the ending of the stream. The
/// two travel as one type so the writer consumes a single sequence and cannot forget the ending.
/// </summary>
/// <param name="Line">The log line, when this frame is a line; ignored for an ending.</param>
/// <param name="Historical">Whether that line was replayed rather than appended live.</param>
/// <param name="EndReason">
/// The reason the stream ended, or <c>null</c> when this frame is a line. A sequence of frames ends
/// with exactly one frame whose reason is set.
/// </param>
/// <param name="EndMessage">The localized sentence accompanying the ending, or <c>null</c>.</param>
public sealed record SiteLogFrame(string Line, bool Historical, SiteLogEndReason? EndReason, string? EndMessage)
{
    /// <summary>Builds a frame carrying one log line.</summary>
    /// <param name="line">The log line, without its trailing newline.</param>
    /// <param name="historical">Whether the line was replayed rather than appended live.</param>
    /// <returns>The line frame.</returns>
    public static SiteLogFrame OfLine(string line, bool historical)
    {
        return new SiteLogFrame(line, historical, null, null);
    }

    /// <summary>Builds the frame that ends a stream.</summary>
    /// <param name="reason">Why the stream ended.</param>
    /// <param name="message">The localized sentence to show beside it, or <c>null</c>.</param>
    /// <returns>The terminal frame.</returns>
    public static SiteLogFrame OfEnd(SiteLogEndReason reason, string? message)
    {
        return new SiteLogFrame(string.Empty, false, reason, message);
    }
}
