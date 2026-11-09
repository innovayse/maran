using Maran.Agent.Simulated.Interfaces;
using Maran.Agent.Simulated.Services.PhpService;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Seeds;

/// <summary>
/// The state every simulated host starts with, whatever else is seeded on top: one PHP version
/// installed and marked as the default, as a freshly provisioned server has.
/// </summary>
/// <remarks>
/// Registered by <c>AddSimulatedAgent</c> before any seed a host adds, so a later seed can install
/// more versions or add accounts without having to remember the floor.
/// </remarks>
public sealed class SimulatedBaselineSeed : ISimulatedAgentSeed
{
    /// <summary>The PHP version a fresh host ships with.</summary>
    public const string DefaultPhpVersion = "8.3";

    /// <summary>The directory every simulated FPM runtime reports its sockets in.</summary>
    public const string FpmSocketDirectory = "/run/maran-php";

    /// <inheritdoc />
    public void ApplyTo(SimulatedAgentLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        ledger.PhpRuntimes.Add(
            DefaultPhpVersion,
            new SimulatedPhpRuntime(
                DefaultPhpVersion,
                FpmSocketDirectory,
                true,
                SimulatedPhpCatalog.DefaultEnabledExtensions));
    }
}
