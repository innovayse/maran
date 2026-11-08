using Maran.Host.Demo.Guard.Enums;

namespace Maran.Host.Demo.Guard;

/// <summary>The guard's answer for one request.</summary>
/// <param name="Permitted">Whether the request may go on.</param>
/// <param name="Area">The area that explains a refusal; <c>null</c> when permitted.</param>
/// <param name="CountsTowardBudget">Whether a permitted request spends the mutation budgets.</param>
/// <param name="CountsTowardSignInBudget">Whether a permitted request draws on the sign-in allowances.</param>
public sealed record DemoGuardDecision(
    bool Permitted,
    DemoRefusalArea? Area,
    bool CountsTowardBudget,
    bool CountsTowardSignInBudget = false)
{
    /// <summary>A request that is not a mutation, or one the demo does not count.</summary>
    public static readonly DemoGuardDecision PassUncounted = new(true, null, false);

    /// <summary>A mutation that may run and spends the budgets.</summary>
    public static readonly DemoGuardDecision PassCounted = new(true, null, true);

    /// <summary>A sign-in, refresh or sign-out that may run and draws on the sign-in allowances.</summary>
    public static readonly DemoGuardDecision PassSignIn = new(true, null, false, true);

    /// <summary>A refusal.</summary>
    /// <param name="area">The area that explains it.</param>
    /// <returns>The decision.</returns>
    public static DemoGuardDecision Refuse(DemoRefusalArea area)
    {
        return new DemoGuardDecision(false, area, false);
    }
}
