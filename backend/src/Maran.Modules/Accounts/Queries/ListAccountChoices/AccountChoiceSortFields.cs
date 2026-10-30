namespace Maran.Modules.Accounts.Queries.ListAccountChoices;

/// <summary>
/// The public names the account picker sorts by — the <c>sort</c> parameter's vocabulary, written
/// once and read by <see cref="ListAccountChoicesSorts.Map"/> and the tests.
/// </summary>
public static class AccountChoiceSortFields
{
    /// <summary>The account's name.</summary>
    public const string Name = "name";
}
