using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>A clock that reads the instant it was set to, so no test depends on the time it runs at.</summary>
public sealed class HandlerClock : IClock
{
    /// <summary>The instant every read answers; 2026-11-09 11:00 UTC unless a test moves it.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 9, 11, 0, 0, TimeSpan.Zero);
}
