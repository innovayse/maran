namespace Maran.Modules.ServerMigration.Queries.ListAccountImports;

/// <summary>
/// The public names the account imports list sorts by — the <c>sort</c> parameter's vocabulary, written once and read
/// by <see cref="ListAccountImportsSorts.Map"/> and the tests.
/// </summary>
public static class AccountImportSortFields
{
    /// <summary>The imported account's name.</summary>
    public const string AccountName = "accountName";

    /// <summary>The name the sending server gave itself.</summary>
    public const string SourceHostName = "sourceHostName";

    /// <summary>How far the import has got.</summary>
    public const string State = "state";

    /// <summary>The artifact's size.</summary>
    public const string SizeBytes = "sizeBytes";

    /// <summary>When the package was accepted.</summary>
    public const string CreatedAt = "createdAt";
}
