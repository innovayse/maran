using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Updates.Tests.TestSupport;

/// <summary>A clock the test moves by hand, because the ambient one is a banned API.</summary>
public sealed class FakeClock : IClock
{
    /// <summary>The instant every read returns until a test advances it.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
}
