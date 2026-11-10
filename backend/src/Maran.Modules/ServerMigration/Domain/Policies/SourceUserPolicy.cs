
namespace Maran.Modules.ServerMigration.Domain.Policies;

/// <summary>
/// What the panel accepts as the login on a remote server: the shape of a Linux system user name. The agent holds
/// the same shape again before it connects.
/// </summary>
public static class SourceUserPolicy
{
    /// <summary>The spelling stored and compared: surrounding space removed. A login is case sensitive and is not lowered.</summary>
    /// <param name="user">The login as the administrator typed it.</param>
    /// <returns>The normalised login.</returns>
    public static string Normalize(string user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.Trim();
    }

    /// <summary>Whether a normalised login is <c>[a-z_][a-z0-9_-]{0,31}</c>.</summary>
    /// <param name="user">The login after <see cref="Normalize"/>.</param>
    /// <returns>True for a well-formed login.</returns>
    public static bool IsValid(string? user)
    {
        if (string.IsNullOrEmpty(user) || user.Length > RemoteServerFieldLimits.SshUserMaxLength)
        {
            return false;
        }

        if (!IsLeading(user[0]))
        {
            return false;
        }

        return user.Skip(1).All(character =>
        {
            return char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character == '_'
                || character == '-';
        });
    }

    /// <summary>Whether a character may begin a login: a lower-case letter or an underscore.</summary>
    /// <param name="character">The first character.</param>
    /// <returns>True when it may begin a login.</returns>
    private static bool IsLeading(char character)
    {
        return char.IsAsciiLetterLower(character) || character == '_';
    }
}
