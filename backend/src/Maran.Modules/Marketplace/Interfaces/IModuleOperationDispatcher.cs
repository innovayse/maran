using Maran.Modules.Marketplace.Jobs;

namespace Maran.Modules.Marketplace.Interfaces;

/// <summary>
/// Hands an accepted module operation to the worker that runs it. A seam of its own so the command
/// handlers do not depend on the message bus, and a test can see what was handed over and run it itself.
/// </summary>
public interface IModuleOperationDispatcher
{
    /// <summary>Queues the operation to run after the request has been answered.</summary>
    /// <param name="request">The accepted operation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the operation is queued, not when it has run.</returns>
    Task DispatchAsync(ModuleOperationRequested request, CancellationToken ct);
}
