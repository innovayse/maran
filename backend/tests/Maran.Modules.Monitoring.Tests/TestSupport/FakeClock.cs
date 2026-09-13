using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>A clock the test moves by hand, because the ambient one is a banned API.</summary>
public sealed class FakeClock : IClock
{
    /// <summary>The instant every read returns until a test advances it.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Moves the clock forward.</summary>
    /// <param name="span">How far to move it.</param>
    public void Advance(TimeSpan span)
    {
        UtcNow += span;
    }
}
