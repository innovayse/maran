using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Seeds;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Tests.TestSupport;

/// <summary>Builds the simulated hosts the first six services' tests run against.</summary>
public static class SimulatedHosts
{
    /// <summary>The system user name of the account most tests host their sites under.</summary>
    public const string AccountName = "acme";

    /// <summary>Builds an empty ledger with only the baseline seed applied, as the container builds it.</summary>
    /// <param name="limits">The caps to build it with; the defaults when null.</param>
    /// <returns>The ledger.</returns>
    public static SimulatedAgentLedger NewLedger(SimulatedAgentLimits? limits = null)
    {
        var ledger = new SimulatedAgentLedger(limits ?? SimulatedAgentLimits.Default);
        new SimulatedBaselineSeed().ApplyTo(ledger);

        return ledger;
    }

    /// <summary>Puts an account straight into the ledger, bypassing the accounts client.</summary>
    /// <param name="ledger">The ledger to write to.</param>
    /// <param name="username">The account's system user name.</param>
    /// <param name="createdAt">When the account is said to have been created.</param>
    /// <returns>The account that was stored.</returns>
    public static SimulatedAccount AddAccount(SimulatedAgentLedger ledger, string username, DateTimeOffset createdAt)
    {
        var account = new SimulatedAccount(
            username,
            ledger.NextUid(),
            "/home/" + username,
            0,
            createdAt,
            false,
            SimulatedAccount.UnlimitedSlices,
            null);
        ledger.Accounts.Add(username, account);

        return account;
    }
}
