using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>An <see cref="IClock"/> whose time only moves when a test moves it.</summary>
public sealed class MovableClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UnixEpoch;

    /// <summary>Moves time forward.</summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by)
    {
        UtcNow += by;
    }
}
