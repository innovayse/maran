using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>A clock that stands still until a test moves it, so a wait of 48 hours is decidable without sleeping.</summary>
public sealed class AdvancingClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far; never negative.</param>
    public void Advance(TimeSpan by)
    {
        UtcNow += by;
    }
}
