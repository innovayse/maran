using Maran.SharedKernel.Interfaces;

namespace Maran.Agent.Client.Tests.TestSupport;

/// <summary>An <see cref="IClock"/> that reports one chosen instant, so validity checks are exact.</summary>
internal sealed class FixedClock : IClock
{
    /// <summary>The instant this clock reports.</summary>
    public DateTimeOffset UtcNow { get; }

    /// <summary>Creates a clock stopped at <paramref name="now"/>.</summary>
    /// <param name="now">The instant the clock reports.</param>
    public FixedClock(DateTimeOffset now)
    {
        UtcNow = now;
    }
}
