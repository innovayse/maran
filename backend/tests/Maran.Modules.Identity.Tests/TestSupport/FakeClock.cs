using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// A deterministic <see cref="IClock"/> double for tests: returns a fixed, caller-supplied instant
/// instead of the ambient clock, which is a banned API in production code (rules/csharp.md).
/// Time moves only when a test says so, which is how expiry can be tested without waiting.
/// </summary>
public sealed class FakeClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>Creates a clock that reports <paramref name="utcNow"/> until it is advanced.</summary>
    /// <param name="utcNow">The instant this clock starts at.</param>
    public FakeClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    /// <summary>Moves the clock forward.</summary>
    /// <param name="amount">How far forward to move.</param>
    public void Advance(TimeSpan amount)
    {
        UtcNow = UtcNow.Add(amount);
    }
}
