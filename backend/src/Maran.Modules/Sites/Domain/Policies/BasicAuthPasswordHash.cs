using System.Text;
using BCryptHasher = BCrypt.Net.BCrypt;

namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// Turns a basic-authentication password into the hash the web server checks it against, and says
/// whether a stored value is such a hash.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why bcrypt, and why here.</b> The web server verifies a credential file with the system's
/// <c>crypt(3)</c>, which understands a fixed set of formats. bcrypt (<c>$2y$</c>) is the only one of
/// them that is not home-grown crypto, MD5 or SHA-1 (rules/security.md item 9), and the .NET base
/// library has no primitive for it, so the panel carries a vetted implementation (BCrypt.Net-Next, MIT)
/// rather than writing one. The agent never sees a password: it is handed this hash, checks its shape
/// and writes it.
/// </para>
/// <para>
/// <b>Why this is not <c>Security/</c>.</b> It has a dial (the cost) and a salt, which the
/// folder rules would send to <c>SharedKernel/Security</c>, but its correctness is defined by the web
/// server's credential format — a specification only the Sites module answers for — and no other
/// module has a use for it.
/// </para>
/// </remarks>
public static class BasicAuthPasswordHash
{
    /// <summary>
    /// The bcrypt cost: 2^12 rounds. The agent accepts 10 to 14, and this sits in the middle so a hash
    /// is slow enough to resist an offline guess and a request that sets ten passwords still returns.
    /// </summary>
    public const int Cost = 12;

    /// <summary>The bcrypt revision written: <c>$2y$</c>, the one <c>crypt(3)</c> implementations agree on.</summary>
    private const char Revision = 'y';

    /// <summary>The prefix every hash this type writes starts with, derived from the revision and the cost so they cannot disagree.</summary>
    private static readonly string Prefix = $"$2{Revision}${Cost}$";

    /// <summary>Hashes a password with a fresh random salt.</summary>
    /// <param name="password">The plain password; already checked for length by the validator.</param>
    /// <returns>The bcrypt hash, 60 characters starting with <c>$2y$12$</c>.</returns>
    public static string Hash(string password)
    {
        return BCryptHasher.HashPassword(password, BCryptHasher.GenerateSalt(Cost, Revision));
    }

    /// <summary>Whether a value read from storage is a hash this type could have written.</summary>
    /// <param name="value">The stored value.</param>
    /// <returns>True for exactly the 60-character form <see cref="Hash"/> produces.</returns>
    public static bool IsHash(string? value)
    {
        return value is not null && value.Length == 60 && value.StartsWith(Prefix, StringComparison.Ordinal);
    }

    /// <summary>Whether a password is within the length bcrypt reads and the panel accepts.</summary>
    /// <param name="password">The candidate password.</param>
    /// <returns>True for 8 characters up to 72 UTF-8 bytes.</returns>
    public static bool IsAcceptablePassword(string? password)
    {
        return password is not null
            && password.Length >= SiteOptionsLimits.MinimumPasswordLength
            && Encoding.UTF8.GetByteCount(password) <= SiteOptionsLimits.MaximumPasswordBytes;
    }
}
