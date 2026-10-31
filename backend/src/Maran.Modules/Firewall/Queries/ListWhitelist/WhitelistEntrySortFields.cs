namespace Maran.Modules.Firewall.Queries.ListWhitelist;

/// <summary>
/// The public names the whitelist sorts by — the <c>sort</c> parameter's vocabulary, written once and
/// read by <see cref="ListWhitelistSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class WhitelistEntrySortFields
{
    /// <summary>The exempt range, exactly as it was written.</summary>
    public const string Cidr = "cidr";

    /// <summary>When the row was added.</summary>
    public const string CreatedAt = "createdAt";
}
