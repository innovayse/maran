namespace Maran.Host.Dashboard.Frames;

/// <summary>
/// The first event of every stream: what the server is about to send and the limits the client should
/// plan around.
/// </summary>
/// <param name="Protocol">The wire version of this stream; additive changes do not bump it.</param>
/// <param name="Audience">Who the stream was opened for.</param>
/// <param name="IntervalMs">How often a host reading is published, in milliseconds.</param>
/// <param name="ServerTime">The server's clock when the stream opened, for a client to measure its skew from.</param>
/// <param name="MaxLifetimeSec">How long the stream lives before an <c>end</c> frame with reason <c>renew</c>.</param>
public sealed record HelloFrame(
    int Protocol,
    StreamAudience Audience,
    int IntervalMs,
    DateTimeOffset ServerTime,
    int MaxLifetimeSec) : DashboardStreamFrame
{
    /// <summary>The event name.</summary>
    public const string Name = "hello";

    /// <inheritdoc />
    public override string EventName()
    {
        return Name;
    }
}
