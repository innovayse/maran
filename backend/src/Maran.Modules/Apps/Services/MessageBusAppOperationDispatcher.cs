using Maran.Modules.Apps.Interfaces;
using Maran.Modules.Apps.Jobs;
using Wolverine;

namespace Maran.Modules.Apps.Services;

/// <summary>
/// Queues an app operation on the panel's message bus, whose local queue runs it on a worker that stops
/// with the host. The handler that receives it is <see cref="AppOperationHandler"/>.
/// </summary>
public sealed class MessageBusAppOperationDispatcher : IAppOperationDispatcher
{
    /// <summary>The panel's message bus.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the dispatcher.</summary>
    /// <param name="bus">The panel's message bus.</param>
    public MessageBusAppOperationDispatcher(IMessageBus bus)
    {
        _bus = bus;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(AppOperationRequested request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Published after the task row and the ledger row are committed (rules/csharp.md "A message is
        // published AFTER the commit"). The loss this accepts: a process that dies between those commits
        // and this line leaves a task that never finishes and a row that says "installing"; the start-up
        // passes close both, and the administrator asks again.
        await _bus.PublishAsync(request);
    }
}
