using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Jobs;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>A dispatcher that keeps the operations it was handed instead of queueing them, so a test runs them itself.</summary>
public sealed class RecordingModuleOperationDispatcher : IModuleOperationDispatcher
{
    /// <summary>The operations handed over, in order.</summary>
    public List<ModuleOperationRequested> Dispatched { get; } = [];

    /// <summary>When true, the hand-over fails as a broken bus would.</summary>
    public bool Throws { get; set; }

    /// <inheritdoc />
    public Task DispatchAsync(ModuleOperationRequested request, CancellationToken ct)
    {
        if (Throws)
        {
            throw new InvalidOperationException("The bus is down.");
        }

        Dispatched.Add(request);
        return Task.CompletedTask;
    }
}
