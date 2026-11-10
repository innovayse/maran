namespace Maran.Modules.ServerMigration.Models;

/// <summary>
/// How reading a remote server's inventory ended, as the discovery handler needs to branch on it: everything the
/// stream reported (<see cref="CollectedInventory"/>), or the failure it ended with
/// (<see cref="FailedInventory"/>). Nothing leaves the handler; it is a carrier between two of its steps.
/// </summary>
public abstract record InventoryOutcome
{
    /// <summary>Closes the hierarchy to this assembly.</summary>
    private protected InventoryOutcome()
    {
    }
}
