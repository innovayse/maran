namespace Maran.Modules.Accounts.Domain.Policies;

/// <summary>
/// What the panel accepts as a reseller's name: the account name alphabet and length, and never a
/// name the system reserves.
/// </summary>
/// <remarks>
/// A reseller's name is also the name of the login created for it, and a login name is one namespace
/// across the panel with account names, so the two are held to the same alphabet: a name valid for one
/// is then never a surprise for the other, and a name an account could not have is not one a reseller
/// can take either. The reserved list is <see cref="ReservedAccountNamePolicy"/>'s, not a copy.
/// <para>
/// A character loop and not a pattern: there is nothing to backtrack, and no anchor (<c>$</c> matches
/// before a trailing newline in .NET) that could be written wrong.
/// </para>
/// </remarks>
public static class ResellerNamePolicy
{
    /// <summary>The shortest reseller name, in characters.</summary>
    public const int MinimumLength = 3;

    /// <summary>The longest reseller name, in characters.</summary>
    public const int MaximumLength = 30;

    /// <summary>Whether <paramref name="name"/> is an acceptable reseller name.</summary>
    /// <param name="name">The name as supplied.</param>
    /// <returns>
    /// True when it is <see cref="MinimumLength"/> to <see cref="MaximumLength"/> lowercase letters,
    /// digits or underscores, starts with a letter and is not reserved.
    /// </returns>
    public static bool IsAcceptable(string? name)
    {
        if (name is null || name.Length < MinimumLength || name.Length > MaximumLength)
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(name[0]))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        return !ReservedAccountNamePolicy.IsReserved(name);
    }
}
