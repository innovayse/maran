using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>A clock frozen at one instant, so serials and timestamps are decidable.</summary>
public sealed class FixedClock : IClock
{
    /// <summary>The instant every read returns.</summary>
    public DateTimeOffset UtcNow { get; } = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
}
