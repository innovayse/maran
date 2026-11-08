namespace Maran.Host.Dashboard.Frames;

/// <summary>The last event of a stream the server ends on purpose.</summary>
/// <param name="Reason">Why it ended, which tells the client whether to reopen silently.</param>
public sealed record EndFrame(StreamEndReason Reason) : DashboardStreamFrame
{
    /// <summary>The event name.</summary>
    public const string Name = "end";

    /// <inheritdoc />
    public override string EventName()
    {
        return Name;
    }
}
