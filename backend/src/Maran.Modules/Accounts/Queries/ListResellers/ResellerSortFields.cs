namespace Maran.Modules.Accounts.Queries.ListResellers;

/// <summary>
/// The public names the resellers list sorts by — the <c>sort</c> parameter's vocabulary, written once
/// and read by <see cref="ListResellersSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class ResellerSortFields
{
    /// <summary>The reseller's name.</summary>
    public const string Name = "name";

    /// <summary>When the reseller was created.</summary>
    public const string CreatedAt = "createdAt";

    /// <summary>The reseller's state.</summary>
    public const string Status = "status";
}
