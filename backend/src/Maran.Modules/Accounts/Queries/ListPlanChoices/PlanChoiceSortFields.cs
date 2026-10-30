namespace Maran.Modules.Accounts.Queries.ListPlanChoices;

/// <summary>
/// The public names the plan picker sorts by — the <c>sort</c> parameter's vocabulary, written once
/// and read by <see cref="ListPlanChoicesSorts.Map"/> and the tests.
/// </summary>
public static class PlanChoiceSortFields
{
    /// <summary>The plan's name, as the caller reads it in their own language.</summary>
    public const string Name = "name";
}
