namespace Maran.Host.Demo.Guard.Enums;

/// <summary>The answer of <see cref="DemoMutationBudget"/> to one counted mutation.</summary>
public enum DemoBudgetDecision
{
    /// <summary>The mutation may run; one unit was spent.</summary>
    Granted,

    /// <summary>The visitor made too many mutations in the current window; nothing was spent.</summary>
    VisitorRateExceeded,

    /// <summary>The demo's whole budget for this reset window is spent; nothing is spent now.</summary>
    GlobalExhausted,
}
