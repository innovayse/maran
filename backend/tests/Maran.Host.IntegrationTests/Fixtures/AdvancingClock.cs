using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Time;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A clock that starts at the real current instant and moves only when a test says so. It starts at
/// the real instant because the bearer handler checks a token's lifetime against the real clock, so a
/// token minted at a fictional instant would be refused before the panel's own checks ran; it moves on
/// command so a test can pass the panel's own deadlines without sleeping (rules/testing.md).
/// </summary>
public sealed class AdvancingClock : IClock
{
    /// <summary>The real instant the clock was created at.</summary>
    private readonly DateTimeOffset _start = new SystemClock().UtcNow;

    /// <summary>How far the test has moved the clock past the real start.</summary>
    private TimeSpan _offset = TimeSpan.Zero;

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get
        {
            return _start + _offset;
        }
    }

    /// <summary>Moves the clock forward.</summary>
    /// <param name="amount">How far.</param>
    public void Advance(TimeSpan amount)
    {
        _offset += amount;
    }
}
