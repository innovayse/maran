namespace Maran.Modules.Accounts.Queries.ListResellerChoices;

/// <summary>
/// The public names the reseller picker sorts by — the <c>sort</c> parameter's vocabulary, written once
/// and read by <see cref="ListResellerChoicesSorts.Map"/> and the tests.
/// </summary>
public static class ResellerChoiceSortFields
{
    /// <summary>The reseller's name.</summary>
    public const string Name = "name";
}
