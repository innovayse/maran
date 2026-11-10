namespace Maran.Modules.Apps.Domain.Enums;

/// <summary>What the start-up reconciliation does with one ledger row, given what the host reports.</summary>
public enum AppLedgerVerdict
{
    /// <summary>The row already says what the host says.</summary>
    Keep = 1,

    /// <summary>The host has the app: the row becomes installed, at the host's version.</summary>
    MarkInstalled = 2,

    /// <summary>The host does not have the app and an install had been started: the row says it failed.</summary>
    MarkFailed = 3,

    /// <summary>The host does not have the app and nothing is pending: the row is removed.</summary>
    Delete = 4,
}
