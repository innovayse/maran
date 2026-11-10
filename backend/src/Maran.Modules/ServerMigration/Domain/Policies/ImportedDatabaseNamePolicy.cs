namespace Maran.Modules.ServerMigration.Domain.Policies;

/// <summary>
/// Decides which of a source's databases can be imported under the name it has, and what the Databases module is
/// asked to create (decision D1: a name is never renamed).
/// </summary>
/// <remarks>
/// A database on this panel is always <c>&lt;account&gt;_&lt;suffix&gt;</c> with a suffix of lowercase letters and
/// digits: the Databases module applies the prefix itself and refuses anything else, so that no account can
/// occupy another's namespace. A source database that does not have that shape cannot be created under its own
/// name; the dry run says so, as a conflict, instead of renaming it (a rename would break the application that
/// names the database in its configuration, and a dump that names its own database would stop matching).
/// </remarks>
public static class ImportedDatabaseNamePolicy
{
    /// <summary>The longest suffix this panel creates; the Databases module judges the total again.</summary>
    private const int SuffixMaxLength = 40;

    /// <summary>The part after the owner's prefix, or <c>null</c> when the database cannot keep its name.</summary>
    /// <param name="accountName">The account's system user name, which is also the database prefix.</param>
    /// <param name="databaseName">The database's name on the source.</param>
    /// <returns>The suffix the Databases module is asked to create, or <c>null</c>.</returns>
    public static string? SuffixOf(string accountName, string databaseName)
    {
        ArgumentNullException.ThrowIfNull(accountName);
        ArgumentNullException.ThrowIfNull(databaseName);

        var prefix = accountName + "_";
        if (!databaseName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var suffix = databaseName[prefix.Length..];

        return suffix.Length is > 0 and <= SuffixMaxLength && suffix.All(IsSuffixCharacter) ? suffix : null;
    }

    /// <summary>Whether a character may be in a suffix.</summary>
    /// <param name="character">The character.</param>
    /// <returns><c>true</c> for a lowercase ASCII letter or a digit.</returns>
    private static bool IsSuffixCharacter(char character)
    {
        return character is >= 'a' and <= 'z' or >= '0' and <= '9';
    }
}
