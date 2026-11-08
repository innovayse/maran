using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Interfaces;

/// <summary>
/// A source of the state the simulated agent starts with. The container applies every registered
/// seed, in registration order, to the ledger when it is first built.
/// </summary>
/// <remarks>
/// The seam exists so the state a demonstration starts from is DEFINED ONCE, beside the database
/// rows it must agree with, and not duplicated inside this project. A seed writes through the
/// ledger's sections exactly as a client would; it bypasses no cap, so a seed larger than the
/// limits fails loudly in its own tests instead of quietly truncating.
/// </remarks>
public interface ISimulatedAgentSeed
{
    /// <summary>Writes this seed's state into <paramref name="ledger"/>.</summary>
    /// <param name="ledger">The ledger the simulated clients read and write.</param>
    void ApplyTo(SimulatedAgentLedger ledger);
}
