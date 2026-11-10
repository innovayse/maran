namespace Maran.Modules.ServerMigration.Queries.ListAccountExports;

/// <summary>
/// The public names the account exports list sorts by — the <c>sort</c> parameter's vocabulary, written once and read
/// by <see cref="ListAccountExportsSorts.Map"/> and the tests.
/// </summary>
public static class AccountExportSortFields
{
    /// <summary>The exported account's name.</summary>
    public const string AccountName = "accountName";

    /// <summary>The artifact's size.</summary>
    public const string SizeBytes = "sizeBytes";

    /// <summary>When the export was made.</summary>
    public const string CreatedAt = "createdAt";

    /// <summary>When the export expires.</summary>
    public const string ExpiresAt = "expiresAt";
}
