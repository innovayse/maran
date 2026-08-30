using Maran.Modules.Identity.Domain.Enums;
namespace Maran.Modules.Identity.Domain;

/// <summary>
/// A panel login: the person who signs in, as distinct from the hosting account they may own
/// (spec §8). Holds only a password <em>hash</em> — the plaintext never reaches this type.
/// </summary>
public sealed class User
{
    /// <summary>
    /// Consecutive failures that lock the account. Deliberately larger than the per-address rate
    /// limit: this is the last line, for an attacker with enough addresses to walk past the first
    /// one, and it must not fire on somebody who simply mistyped a few times.
    /// </summary>
    public const int MaxFailedLoginAttempts = 10;

    /// <summary>
    /// How long a locked account stays locked. A fixed window, not an escalating one: the spec
    /// asks for escalation and for the whole policy to be operator-configurable, and both arrive
    /// with the settings module. A constant here is honest about that; a half-built escalation
    /// curve nobody can tune would not be.
    /// </summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>The user's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The unique login name.</summary>
    public string Username { get; private set; }

    /// <summary>The unique contact address.</summary>
    public string Email { get; private set; }

    /// <summary>The Argon2id hash of the user's password. Never the password itself.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>What this user is allowed to reach.</summary>
    public UserRole Role { get; private set; }

    /// <summary>
    /// The hosting account this user owns, for a <see cref="UserRole.Customer"/>; null for an
    /// administrator, who owns none and reaches all of them.
    /// </summary>
    public Guid? AccountId { get; private set; }

    /// <summary>The TOTP shared secret, base32-encoded and encrypted at rest; null when 2FA is off.</summary>
    public string? TotpSecret { get; private set; }

    /// <summary>True once the user has confirmed a TOTP enrolment.</summary>
    public bool IsTotpEnabled { get; private set; }

    /// <summary>The instant the user was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The instant of the most recent successful login; null before the first one.</summary>
    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>
    /// The TOTP time step of the most recently accepted code, or null when none has been accepted.
    /// Stored so the same code cannot be used twice inside the thirty seconds it stays valid — an
    /// attacker who reads one off a screen or a keylogger otherwise has the rest of that window.
    /// </summary>
    public long? LastTotpWindow { get; private set; }

    /// <summary>
    /// Consecutive failed sign-in attempts since the last successful one. Reset by
    /// <see cref="RecordLogin"/>, never by the passage of time on its own.
    /// </summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>
    /// When the account stops being locked, or <c>null</c> when it is not locked. Checked against
    /// <see cref="IClock"/>, never against the machine's ambient clock.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>Creates a user with two-factor authentication not yet enrolled.</summary>
    /// <param name="id">The user's identity.</param>
    /// <param name="username">The unique login name.</param>
    /// <param name="email">The unique contact address.</param>
    /// <param name="passwordHash">The Argon2id hash of the user's password, produced by <see cref="IPasswordHasher"/>.</param>
    /// <param name="role">What this user is allowed to reach.</param>
    /// <param name="createdAt">The instant the user was created, taken from <see cref="IClock"/>.</param>
    public User(Guid id, string username, string email, string passwordHash, UserRole role, DateTimeOffset createdAt)
    {
        Id = id;
        Username = username;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private User()
    {
        Username = string.Empty;
        Email = string.Empty;
        PasswordHash = string.Empty;
    }

    /// <summary>Binds this user to the hosting account they own.</summary>
    /// <param name="accountId">The owned account's identity.</param>
    public void AssignAccount(Guid accountId)
    {
        AccountId = accountId;
    }

    /// <summary>Replaces the stored password hash.</summary>
    /// <param name="passwordHash">The new Argon2id hash.</param>
    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
    }

    /// <summary>Completes a TOTP enrolment, storing the confirmed secret.</summary>
    /// <param name="secret">The base32-encoded shared secret the user has just proved they hold.</param>
    public void EnableTotp(string secret)
    {
        TotpSecret = secret;
        IsTotpEnabled = true;
    }

    /// <summary>
    /// Turns two-factor authentication off. Clears the secret rather than only the flag: a disabled
    /// flag sitting beside a live secret is one accidental write away from being enabled again with
    /// a factor the user believes they have removed.
    /// </summary>
    public void DisableTotp()
    {
        TotpSecret = null;
        IsTotpEnabled = false;
        LastTotpWindow = null;
    }

    /// <summary>
    /// Records that a TOTP code from <paramref name="window"/> was accepted, so it cannot be
    /// accepted again.
    /// </summary>
    /// <param name="window">The time step of the accepted code.</param>
    public void RecordTotpWindow(long window)
    {
        LastTotpWindow = window;
    }

    /// <summary>Whether the account is locked at <paramref name="now"/>.</summary>
    /// <param name="now">The current instant, taken from <see cref="IClock"/>.</param>
    /// <returns><c>true</c> while the lock is in force.</returns>
    public bool IsLockedOut(DateTimeOffset now)
    {
        return LockedUntil is { } until && until > now;
    }

    /// <summary>
    /// Records a failed sign-in, locking the account once the attempts reach
    /// <see cref="MaxFailedLoginAttempts"/>.
    /// </summary>
    /// <param name="at">The instant of the attempt, taken from <see cref="IClock"/>.</param>
    public void RecordFailedLogin(DateTimeOffset at)
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= MaxFailedLoginAttempts)
        {
            LockedUntil = at + LockoutDuration;
        }
    }

    /// <summary>Records a successful login, clearing any failures and any lock.</summary>
    /// <param name="at">The instant of the login, taken from <see cref="IClock"/>.</param>
    public void RecordLogin(DateTimeOffset at)
    {
        LastLoginAt = at;
        FailedLoginAttempts = 0;
        LockedUntil = null;
    }
}
