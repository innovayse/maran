using System.Runtime.CompilerServices;
using Maran.Modules.Sites.Common;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// A frame source that produces nothing and records whether it was ever stopped.
/// </summary>
/// <remarks>
/// It answers the question "was the reader left running?" — the one a leaked stream turns on. A
/// consumer that walks away from an in-flight read without cancelling it leaves this source waiting
/// forever, which on the real path is a tail still pulling a log nobody is watching and, worse, an
/// async enumerator disposed with a move still outstanding.
/// </remarks>
public sealed class CancellationObservingFrameSource
{
    /// <summary>Set when the source was stopped rather than abandoned.</summary>
    public bool Stopped { get; private set; }

    /// <summary>Produces no frames, ending only when cancelled.</summary>
    /// <param name="cancellationToken">Cancelled when the consumer stops reading.</param>
    /// <returns>A sequence that never completes on its own.</returns>
    public async IAsyncEnumerable<SiteLogFrame> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        finally
        {
            Stopped = true;
        }

        yield break;
    }
}
