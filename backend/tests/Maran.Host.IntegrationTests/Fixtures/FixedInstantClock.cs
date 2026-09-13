using Maran.SharedKernel.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IClock"/> reporting one fixed instant, because the ambient clock is a banned symbol
/// in production code and a test must supply the same seam the panel does.
/// </summary>
public sealed class FixedInstantClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; } = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);
}
