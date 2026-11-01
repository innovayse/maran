using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>A clock that reads the instant it was set to, so no test depends on the time it runs at.</summary>
public sealed class FixedClock : IClock
{
    /// <summary>The instant every read answers; 2026-11-01 09:00 UTC unless a test moves it.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
}
