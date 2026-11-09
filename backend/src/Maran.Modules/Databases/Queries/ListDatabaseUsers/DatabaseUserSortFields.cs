namespace Maran.Modules.Databases.Queries.ListDatabaseUsers;

/// <summary>
/// The public names the database users list sorts by: the <c>sort</c> parameter's vocabulary, written
/// once and read by <see cref="ListDatabaseUsersSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class DatabaseUserSortFields
{
    /// <summary>The user's name as MySQL holds it and the list shows it: the full, prefixed name.</summary>
    public const string Name = "name";

    /// <summary>When the user was created.</summary>
    public const string CreatedAt = "createdAt";
}
