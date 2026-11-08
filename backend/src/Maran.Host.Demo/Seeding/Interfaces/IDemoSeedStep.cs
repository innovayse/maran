using Maran.Host.Demo.Seeding.Models;

namespace Maran.Host.Demo.Seeding.Interfaces;

/// <summary>One step of building the demonstration, run in registration order.</summary>
public interface IDemoSeedStep
{
    /// <summary>The step's name, printed as it runs and named in a failure.</summary>
    string Name { get; }

    /// <summary>Runs the step.</summary>
    /// <param name="context">What the steps share.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>A task that completes when the step is done.</returns>
    Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken);
}
