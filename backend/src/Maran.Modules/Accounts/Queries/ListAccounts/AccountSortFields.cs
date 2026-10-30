namespace Maran.Modules.Accounts.Queries.ListAccounts;

/// <summary>
/// The public names the accounts list sorts by — the <c>sort</c> parameter's vocabulary, written once
/// and read by <see cref="ListAccountsSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class AccountSortFields
{
    /// <summary>The account's name.</summary>
    public const string Name = "name";

    /// <summary>When the account was created.</summary>
    public const string CreatedAt = "createdAt";

    /// <summary>The account's state.</summary>
    public const string Status = "status";
}
