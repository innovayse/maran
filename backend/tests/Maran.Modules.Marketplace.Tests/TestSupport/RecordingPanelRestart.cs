using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>A panel restart that records the modules it was asked to restart for instead of restarting.</summary>
public sealed class RecordingPanelRestart : IPanelRestart
{
    /// <summary>The module ids restarts were requested for, in order.</summary>
    public List<string> Requested { get; } = [];

    /// <inheritdoc />
    public Task RequestAsync(string moduleId, CancellationToken cancellationToken)
    {
        Requested.Add(moduleId);
        return Task.CompletedTask;
    }
}
