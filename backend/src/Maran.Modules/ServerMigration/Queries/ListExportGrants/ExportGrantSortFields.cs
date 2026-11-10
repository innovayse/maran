namespace Maran.Modules.ServerMigration.Queries.ListExportGrants;

/// <summary>
/// The public names the pull grants list sorts by — the <c>sort</c> parameter's vocabulary, written once and read by
/// <see cref="ListExportGrantsSorts.Map"/> and the tests.
/// </summary>
public static class ExportGrantSortFields
{
    /// <summary>The exported account's name.</summary>
    public const string AccountName = "accountName";

    /// <summary>When the grant was issued.</summary>
    public const string CreatedAt = "createdAt";

    /// <summary>When the grant stops working.</summary>
    public const string ExpiresAt = "expiresAt";
}
