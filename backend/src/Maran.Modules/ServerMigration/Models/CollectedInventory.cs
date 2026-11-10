using Maran.Agent.Client.Services.TransferService;

namespace Maran.Modules.ServerMigration.Models;

/// <summary>Everything one inventory stream reported before it finished.</summary>
/// <param name="Accounts">The accounts found, in the order the source reported them.</param>
/// <param name="Warnings">What the source could not read or did not report in full.</param>
public sealed record CollectedInventory(
    IReadOnlyList<AgentSourceAccount> Accounts,
    IReadOnlyList<AgentSourceWarning> Warnings) : InventoryOutcome;
