namespace Maran.Modules.Accounts.Queries.ListPlans;

/// <summary>
/// The public names the plans list sorts by — the <c>sort</c> parameter's vocabulary, written once
/// and read by <see cref="ListPlansSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class PlanSortFields
{
    /// <summary>The plan's name, as the caller reads it in their own language.</summary>
    public const string Name = "name";

    /// <summary>The disk quota the plan sells.</summary>
    public const string DiskQuota = "diskQuota";

    /// <summary>How many accounts are on the plan.</summary>
    public const string Accounts = "accounts";
}
