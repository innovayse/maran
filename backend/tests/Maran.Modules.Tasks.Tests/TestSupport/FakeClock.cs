using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Tasks.Tests.TestSupport;

/// <summary>
/// A deterministic <see cref="IClock"/> double for tests: returns a fixed, caller-supplied instant
/// instead of the ambient clock, which is a banned API in production code (rules/csharp.md).
/// </summary>
public sealed class FakeClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; }

    /// <summary>Creates a clock that always reports <paramref name="utcNow"/>.</summary>
    /// <param name="utcNow">The fixed instant this clock reports.</param>
    public FakeClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }
}
