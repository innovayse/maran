namespace Maran.Agent.Simulated.Enums;

/// <summary>How an attempt to add an entry to a ledger section ended.</summary>
public enum SimulatedLedgerAdd
{
    /// <summary>The entry was stored under its key.</summary>
    Added,

    /// <summary>An entry already held the key; nothing was changed.</summary>
    AlreadyExists,

    /// <summary>The section already holds as many entries as its cap allows; nothing was changed.</summary>
    LimitReached,
}
