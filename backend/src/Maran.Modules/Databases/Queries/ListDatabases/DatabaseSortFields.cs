namespace Maran.Modules.Databases.Queries.ListDatabases;

/// <summary>
/// The public names the databases list sorts by — the <c>sort</c> parameter's vocabulary, written
/// once and read by <see cref="ListDatabasesSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class DatabaseSortFields
{
    /// <summary>The database's name as MySQL holds it and the list shows it: the full, prefixed name.</summary>
    public const string Name = "name";

    /// <summary>When the database was created.</summary>
    public const string CreatedAt = "createdAt";
}
