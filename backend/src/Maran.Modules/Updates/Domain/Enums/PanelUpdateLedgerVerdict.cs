namespace Maran.Modules.Updates.Domain.Enums;

/// <summary>What the reaper does with an open update request.</summary>
public enum PanelUpdateLedgerVerdict
{
    /// <summary>Nothing: there is no request, it is still running, or it has not waited long enough to judge.</summary>
    Leave,

    /// <summary>The marker was never picked up: withdraw it and fail the task.</summary>
    NeverStarted,

    /// <summary>The run ended and no report arrived: fail the task as having an unknown outcome.</summary>
    OutcomeUnknown,
}
