using Maran.Modules.Sites.Common;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>A frame source that counts what it has been asked for.</summary>
/// <remarks>
/// Bounded rather than endless, deliberately. An endless source makes "the consumer failed to stop"
/// show up as a test that never finishes, and a hang is a worse result than a failure: it stalls the
/// run instead of naming the defect, and it cannot be scored by a mutation harness at all. The bound
/// is far above the one frame a correct consumer takes, so a consumer that keeps reading is still
/// unmistakable.
/// </remarks>
public sealed class CountingFrameSource
{
    /// <summary>How many frames this source will produce before it ends of its own accord.</summary>
    public const int Available = 50;

    /// <summary>How many frames were pulled from this source.</summary>
    public int Yielded { get; private set; }

    /// <summary>Yields line frames up to <see cref="Available"/>.</summary>
    /// <returns>The sequence of frames.</returns>
    public async IAsyncEnumerable<SiteLogFrame> ReadAsync()
    {
        while (Yielded < Available)
        {
            Yielded++;
            yield return SiteLogFrame.OfLine($"line {Yielded}", historical: false);
            await Task.Yield();
        }
    }
}
