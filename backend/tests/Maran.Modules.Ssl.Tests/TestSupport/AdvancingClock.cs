using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IClock"/> double that moves forward by a fixed step on every read.
/// </summary>
/// <remarks>
/// Needed wherever production code BOUNDS a wait by comparing the clock to a deadline — the ACME
/// poll loops do exactly that. A frozen clock never reaches the deadline, so such a loop spins for
/// ever and the test hangs instead of failing, which is the worst of the two outcomes: a hang has no
/// message and no failing test name. This is also why the step is large enough that a handful of
/// reads crosses any timeout a test configures.
/// </remarks>
public sealed class AdvancingClock : IClock
{
    /// <summary>How far the clock moves on each read.</summary>
    private readonly TimeSpan _step;

    /// <summary>What the next read will report before the step is applied.</summary>
    private DateTimeOffset _now;

    /// <summary>Creates a clock starting at <paramref name="start"/>.</summary>
    /// <param name="start">The instant the first read reports.</param>
    /// <param name="step">How far each read moves the clock; a second by default.</param>
    public AdvancingClock(DateTimeOffset start, TimeSpan? step = null)
    {
        _now = start;
        _step = step ?? TimeSpan.FromSeconds(1);
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get
        {
            var current = _now;
            _now += _step;
            return current;
        }
    }
}
