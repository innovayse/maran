namespace Maran.Host.Demo.Guard;

/// <summary>One counter inside one time window: when the window began and how much was counted in it.</summary>
/// <remarks>Not thread-safe; the table that holds it is locked by whoever owns the table.</remarks>
public sealed class DemoVisitorWindow
{
    /// <summary>When the current window began.</summary>
    public DateTimeOffset Start { get; private set; }

    /// <summary>What was counted in the current window.</summary>
    public int Count { get; private set; }

    /// <summary>Starts a window with nothing counted.</summary>
    /// <param name="start">When the window begins.</param>
    public DemoVisitorWindow(DateTimeOffset start)
    {
        Start = start;
    }

    /// <summary>Whether the window has run its length.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="length">The window length.</param>
    /// <returns><c>true</c> when <paramref name="length"/> has passed since the window began.</returns>
    public bool HasEnded(DateTimeOffset now, TimeSpan length)
    {
        return now - Start >= length;
    }

    /// <summary>How long until the window ends.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="length">The window length.</param>
    /// <returns>The time left, never negative.</returns>
    public TimeSpan TimeLeft(DateTimeOffset now, TimeSpan length)
    {
        var left = Start + length - now;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>Begins a new window with nothing counted.</summary>
    /// <param name="now">When the new window begins.</param>
    public void Restart(DateTimeOffset now)
    {
        Start = now;
        Count = 0;
    }

    /// <summary>Counts one unit in the current window.</summary>
    public void CountOne()
    {
        Count++;
    }
}
