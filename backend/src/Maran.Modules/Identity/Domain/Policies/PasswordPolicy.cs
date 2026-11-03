namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The rules about a password a person chooses, stated once for every path that sets one: how long it
/// may be, how short it may not be, and that it is not the login's own name. Stateless, so the
/// first-administrator validator, the shared <c>NewPasswordValidator</c> and the change-password handler
/// read the same definitions.
/// </summary>
/// <remarks>
/// The minimum is a parameter and not a constant here because it is the operator's to set
/// (<c>SecurityPolicy.MinimumPasswordLength</c>); only the ceiling is fixed, since it bounds the work
/// Argon2id is asked to do for one request and is not policy.
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>Longest password accepted, bounding the work Argon2id is asked to do.</summary>
    public const int MaxLength = 256;

    /// <summary>Whether a password is non-empty and at least the minimum length.</summary>
    /// <param name="password">The password as typed; null is treated as empty.</param>
    /// <param name="minimumLength">The shortest password the panel accepts.</param>
    /// <returns>True when the password is long enough.</returns>
    public static bool MeetsMinimumLength(string? password, int minimumLength)
    {
        return !string.IsNullOrEmpty(password) && password.Length >= minimumLength;
    }

    /// <summary>Whether a password is no longer than the ceiling.</summary>
    /// <param name="password">The password as typed; null is within the ceiling.</param>
    /// <returns>True when the password is not longer than <see cref="MaxLength"/>.</returns>
    public static bool IsWithinMaximumLength(string? password)
    {
        return password is null || password.Length <= MaxLength;
    }

    /// <summary>Whether a password is something other than the login's own username.</summary>
    /// <param name="password">The password as typed.</param>
    /// <param name="username">The login's username.</param>
    /// <returns>True when the password differs from the username (ordinal comparison).</returns>
    public static bool DiffersFromUsername(string? password, string? username)
    {
        return !string.Equals(password, username, StringComparison.Ordinal);
    }
}
