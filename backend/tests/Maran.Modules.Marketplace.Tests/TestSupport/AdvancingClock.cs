using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>A clock that stands still until a test moves it, so a wait of half an hour is decidable without sleeping.</summary>
public sealed class AdvancingClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far; never negative.</param>
    public void Advance(TimeSpan by)
    {
        UtcNow += by;
    }
}
