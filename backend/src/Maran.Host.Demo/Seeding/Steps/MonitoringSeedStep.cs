using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Modules.Monitoring.Commands.SaveAlertThresholds;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>Saves the alert thresholds the monitoring screen shows.</summary>
public sealed class MonitoringSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public MonitoringSeedStep(DemoSeedCommandRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "monitoring"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        await _runner.RunAsync(
            Name,
            new SaveAlertThresholdsCommand(
                context.World.DiskAlertPercent,
                context.World.CpuAlertPercent,
                context.World.MemoryAlertPercent,
                DemoSeedCommandRunner.Origin,
                DemoSeedCommandRunner.Agent),
            cancellationToken);
    }
}
