namespace Maran.Modules.Updates.Domain.Enums;

/// <summary>What happened when the panel tried to record the task an update request opened.</summary>
public enum PanelUpdateLedgerOutcome
{
    /// <summary>The ledger now names the task.</summary>
    Written,

    /// <summary>A ledger already exists: another request's task is still open, and it was left untouched.</summary>
    AlreadyOpen,

    /// <summary>The ledger could not be written (the directory is missing or not writable).</summary>
    Failed,
}
