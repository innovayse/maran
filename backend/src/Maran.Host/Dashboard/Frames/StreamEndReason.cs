namespace Maran.Host.Dashboard.Frames;

/// <summary>Why the server ended a dashboard stream, as the terminal <c>end</c> frame states it.</summary>
public enum StreamEndReason
{
    /// <summary>
    /// The stream reached its maximum lifetime. The client reopens it at once and silently, which
    /// re-checks its access token: this is what bounds how long a revoked token keeps a stream alive.
    /// </summary>
    Renew,

    /// <summary>The panel is stopping. The client reopens like any other lost connection.</summary>
    Shutdown,
}
