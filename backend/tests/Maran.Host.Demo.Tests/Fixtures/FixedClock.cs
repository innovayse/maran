using Maran.SharedKernel.Interfaces;

namespace Maran.Host.Demo.Tests.Fixtures;

/// <summary>A clock a test moves by hand.</summary>
public sealed class FixedClock : IClock
{
    /// <summary>The instant every read answers; tests assign it to move time.</summary>
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
}
