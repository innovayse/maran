namespace Maran.Sdk.Contracts;

/// <summary>
/// The agent's absolute bounds for the customer's PHP settings: what the root agent refuses to write
/// whatever a plan says. The panel's copy, kept equal to <c>maran_ops::php::PhpOverride</c>'s table.
/// </summary>
/// <remarks>
/// A plan's own bound (<see cref="PhpLimitsSnapshot"/>) is always at or below these. They are the
/// agent's wall, not the plan's: a plan states what it sells, the agent states what a pool may ever
/// carry. The two sides must stay equal and the agent's value decides.
/// </remarks>
public static class PhpSettingCeilings
{
    /// <summary>The largest figure any of the three size settings may be given, in mebibytes.</summary>
    public const int MaxSizeMb = 512;

    /// <summary>The longest execution limit, in seconds. Above it a request is stuck, not slow.</summary>
    public const int MaxExecutionSeconds = 300;

    /// <summary>The largest input-variable count.</summary>
    public const int MaxInputVars = 10_000;
}
