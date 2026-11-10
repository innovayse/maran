using Maran.Modules.Marketplace.Services;

namespace Maran.Modules.Marketplace.Jobs;

/// <summary>
/// Receives an accepted module operation from the panel's message bus and runs it. All the work and all the
/// cleanup are in <see cref="ModuleOperationRunner"/>; this type exists because a Wolverine handler is a
/// type the bus constructs per message, and the runner is the thing a test can drive without a bus.
/// </summary>
public sealed class ModuleOperationHandler
{
    /// <summary>Runs the operation.</summary>
    private readonly ModuleOperationRunner _runner;

    /// <summary>Creates the handler.</summary>
    /// <param name="runner">Runs the operation.</param>
    public ModuleOperationHandler(ModuleOperationRunner runner)
    {
        _runner = runner;
    }

    /// <summary>Runs one accepted operation to its end.</summary>
    /// <param name="message">The accepted operation.</param>
    /// <param name="cancellationToken">Cancellation token; the host's shutdown token.</param>
    /// <returns>A task that completes when the operation has ended.</returns>
    public async Task HandleAsync(ModuleOperationRequested message, CancellationToken cancellationToken)
    {
        await _runner.RunAsync(message, cancellationToken);
    }
}
