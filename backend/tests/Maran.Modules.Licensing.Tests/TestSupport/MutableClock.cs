using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>A clock a test moves by hand, so a cache lifetime is driven without sleeping.</summary>
internal sealed class MutableClock : IClock
{
    /// <summary>Creates the clock at an instant.</summary>
    /// <param name="start">The instant it reads until moved.</param>
    public MutableClock(DateTimeOffset start)
    {
        UtcNow = start;
    }

    /// <summary>The instant the clock reads.</summary>
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by)
    {
        UtcNow += by;
    }
}
