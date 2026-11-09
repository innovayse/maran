using Maran.Agent.Simulated.Interfaces;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Tests.TestSupport;

/// <summary>A seed that adds one account and notes whether the baseline seed had already run.</summary>
public sealed class MarkerSeed : ISimulatedAgentSeed
{
    /// <summary>The name of the account the seed adds.</summary>
    public const string AccountName = "seeded";

    /// <summary>Whether the default PHP version was already installed when this seed ran.</summary>
    public bool SawBaseline { get; private set; }

    /// <inheritdoc />
    public void ApplyTo(SimulatedAgentLedger ledger)
    {
        SawBaseline = ledger.PhpRuntimes.Contains("8.3");
        SimulatedHosts.AddAccount(ledger, AccountName, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
