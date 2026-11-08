using Maran.Sdk.Interfaces;

namespace Maran.Host.IntegrationTests.Modules;

/// <summary>
/// Stands in for the panel's restart. The real one stops the host, which in a test would end the very
/// host under test, so it only records that a restart was asked for and for which module.
/// </summary>
public sealed class RecordingPanelRestart : IPanelRestart
{
    /// <summary>The module ids a restart was requested for, in order.</summary>
    public List<string> Requests { get; } = [];

    /// <inheritdoc />
    public Task RequestAsync(string moduleId, CancellationToken cancellationToken)
    {
        Requests.Add(moduleId);
        return Task.CompletedTask;
    }
}
