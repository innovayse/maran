namespace Maran.Host.Dashboard.Frames;

/// <summary>
/// The back-fill, sent once right after <see cref="HelloFrame"/>: every reading from the instant the
/// client asked for, oldest first, so a chart can be drawn before the first live tick.
/// </summary>
/// <param name="Points">The readings; capped at the ring's capacity, empty when there are none yet.</param>
public sealed record HistoryFrame(IReadOnlyList<MetricsFrame> Points) : DashboardStreamFrame
{
    /// <summary>The event name.</summary>
    public const string Name = "history";

    /// <inheritdoc />
    public override string EventName()
    {
        return Name;
    }
}
