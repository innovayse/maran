using Maran.Modules.Identity.Domain.Enums;
namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// A panel login: the person who signs in, as distinct from the hosting account they may own
/// (spec §8). Holds only a password <em>hash</em> — the plaintext never reaches this type.
/// </summary>
public sealed class User
{
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
    /// Whether this login may be used. An invited login holds no password at all rather than an
    /// empty hash: a sentinel hash in the same field a real one lives in is one careless comparison
    /// away from being accepted.
    /// </summary>
    public UserState State { get; private set; }

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

    /// <summary>
    /// Whether this login's hosting account is currently suspended, tracked independently of
    /// <see cref="State"/> so an <see cref="UserState.Invited"/> login — which <see cref="Suspend"/>
    /// deliberately leaves in <c>Invited</c>, so an unused invitation is never silently consumed —
    /// still remembers that its account was suspended while the invitation was outstanding.
    /// <see cref="Activate"/> reads this flag to decide whether accepting that invitation may
    /// produce a usable login.
    /// </summary>
    public bool IsAccountSuspended { get; private set; }

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

    /// <summary>Creates the login of a hosting account's owner, before they have set a password.</summary>
    /// <param name="id">The login's identity.</param>
    /// <param name="username">The account's system user name, which is also the login name.</param>
    /// <param name="email">The address the invitation was sent to.</param>
    /// <param name="accountId">The hosting account this login owns.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    /// <returns>A login in <see cref="UserState.Invited"/>, which cannot sign in.</returns>
    public static User Invite(Guid id, string username, string email, Guid accountId, DateTimeOffset createdAt)
    {
        var user = new User(id, username, email, string.Empty, UserRole.Customer, createdAt)
        {
            State = UserState.Invited,
        };

        user.AssignAccount(accountId);
        return user;
    }

    /// <summary>
    /// Accepts an invitation: stores the first password and makes the login usable — unless the
    /// account was suspended while the invitation was outstanding, in which case the password is
    /// stored but the login comes out <see cref="UserState.Suspended"/> rather than usable.
    /// </summary>
    /// <param name="passwordHash">The Argon2id hash of the password its owner chose.</param>
    /// <remarks>
    /// A customer must never be able to turn a suspended hosting account into a working login simply
    /// by finding an old invitation link. Storing the password rather than refusing it means the
    /// owner does not have to accept the invitation a second time once the account is resumed —
    /// <see cref="Resume"/> then returns them straight to <see cref="UserState.Active"/> with the
    /// password they already chose.
    /// </remarks>
    public void Activate(string passwordHash)
    {
        PasswordHash = passwordHash;
        State = IsAccountSuspended ? UserState.Suspended : UserState.Active;
    }

    /// <summary>
    /// Blocks sign-in because the hosting account was suspended. Keeps the password.
    /// </summary>
    /// <remarks>
    /// <see cref="State"/> stays <see cref="UserState.Invited"/> for a login that has not been
    /// claimed yet: suspending an account whose owner never set a password must not silently consume
    /// the invitation they have not used yet. <see cref="IsAccountSuspended"/> is set regardless of
    /// the current state, precisely so that fact is not lost when <see cref="State"/> cannot record
    /// it — it is what <see cref="Activate"/> later reads to keep an invitation accepted during a
    /// suspension from producing a usable login.
    /// </remarks>
    public void Suspend()
    {
        if (State == UserState.Active)
        {
            State = UserState.Suspended;
        }

        IsAccountSuspended = true;
    }

    /// <summary>Lifts a suspension, returning the login to the state it was in before.</summary>
    public void Resume()
    {
        if (State == UserState.Suspended)
        {
            State = UserState.Active;
        }

        IsAccountSuspended = false;
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
    /// <paramref name="maxFailedAttempts"/>.
    /// </summary>
    /// <param name="at">The instant of the attempt, taken from <see cref="IClock"/>.</param>
    /// <param name="maxFailedAttempts">Consecutive failures that lock the account, from the panel's security policy.</param>
    /// <param name="lockoutDuration">How long the lock lasts, from the panel's security policy.</param>
    /// <remarks>
    /// The two numbers are passed in rather than being constants on this type. They used to be
    /// constants here, which meant the panel's lockout policy was a recompile: the operator-facing
    /// security policy (<c>SecurityPolicy</c>) is now the one place they are stated, and an entity
    /// that carried its own copy would be a second answer the screen could not change.
    /// </remarks>
    public void RecordFailedLogin(DateTimeOffset at, int maxFailedAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= maxFailedAttempts)
        {
            LockedUntil = at + lockoutDuration;
        }
    }

    /// <summary>
    /// Clears the failure count and any lock, without recording a sign-in.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RecordLogin"/> because a completed password reset is not a login and
    /// must not be dated as one — <c>LastLoginAt</c> is what an operator reads to answer "when was
    /// this account last used", and a reset stamping it would say somebody signed in who did not.
    /// The lock IS cleared: the person who proved control of the mailbox is very often the same
    /// person whose forgotten password locked the account, and leaving them locked out of an account
    /// they have just recovered turns the reset into nothing.
    /// </remarks>
    public void ClearLockout()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
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
