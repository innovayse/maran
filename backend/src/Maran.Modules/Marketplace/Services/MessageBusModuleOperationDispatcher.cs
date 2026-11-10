using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Jobs;
using Wolverine;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Queues a module operation on the panel's message bus, whose local queue runs it on a worker that stops
/// with the host. The handler that receives it is <see cref="ModuleOperationHandler"/>.
/// </summary>
public sealed class MessageBusModuleOperationDispatcher : IModuleOperationDispatcher
{
    /// <summary>The panel's message bus.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the dispatcher.</summary>
    /// <param name="bus">The panel's message bus.</param>
    public MessageBusModuleOperationDispatcher(IMessageBus bus)
    {
        _bus = bus;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(ModuleOperationRequested request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Published after the task row and the operation gate are committed (rules/csharp.md "A message is
        // published AFTER the commit"). The loss this accepts: a process that dies between those commits and
        // this line leaves a task that never finishes and a gate that goes stale after thirty minutes; the
        // administrator sees the task stay running and the next attempt is allowed once the gate is stale.
        await _bus.PublishAsync(request);
    }
}
