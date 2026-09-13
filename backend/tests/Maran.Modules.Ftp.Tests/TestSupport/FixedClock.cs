using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// An <see cref="IClock"/> that never moves, so a stamped row is comparable to a known instant and
/// no test depends on how long it took to run (rules/testing.md "Determinism").
/// </summary>
public sealed class FixedClock : IClock
{
    /// <summary>The instant this clock reports, and the only one it ever will.</summary>
    public DateTimeOffset UtcNow { get; } = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
}
