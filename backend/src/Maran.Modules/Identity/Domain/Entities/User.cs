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
    /// Which of the four real situations this login is in: usable, waiting for its owner to set a
    /// password, waiting while its account is suspended, or suspended. The one stored fact about
    /// whether the login may be used. An invited login holds no password at all rather than an empty
    /// hash: a sentinel hash in the same field a real one lives in is one careless comparison away
    /// from being accepted.
    /// </summary>
    /// <remarks>
    /// Replaces the pair <see cref="LegacyState"/> and <see cref="LegacyAccountSuspended"/>, whose six
    /// combinations included two that no mutator produces. Every mutator below writes this property
    /// and then restates it into that pair, so the previous release, which still reads the pair,
    /// keeps working until the contract release drops it.
    /// </remarks>
    public UserLifecycle Lifecycle { get; private set; }

    /// <summary>
    /// The previous release's coarse state, stored in <c>Users."State"</c>. <b>Legacy: written only
    /// by <see cref="SyncLegacyColumns"/> and never read by this release's code</b>; read
    /// <see cref="Lifecycle"/> instead. Kept for one release because the previous release reads the
    /// column (rules/architecture.md, expand then contract); the next release drops it.
    /// </summary>
    public UserState LegacyState { get; private set; }

    /// <summary>
    /// The previous release's "this login's account is suspended" flag, stored in
    /// <c>Users."IsAccountSuspended"</c>. <b>Legacy: written only by <see cref="SyncLegacyColumns"/>
    /// and never read by this release's code</b>; read <see cref="Lifecycle"/> instead. Dropped by
    /// the same later release as <see cref="LegacyState"/>.
    /// </summary>
    public bool LegacyAccountSuspended { get; private set; }

    /// <summary>
    /// Whether this login may be used to obtain a token at all — the one place that question is
    /// asked, so a new way of minting one cannot be added without asking it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a member here rather than an <c>if</c> in each handler.</b> There were two sign-in
    /// paths, each with its own copy of this comparison, and the two-factor path's copy was missing
    /// — a suspended login with a second factor enrolled could sign in through it. That was fixed by
    /// routing both through <c>AuthenticationCompleter</c>, and the comparison then existed once.
    /// It was not the only door: <c>RefreshSessionCommandHandler</c> mints an access token from an
    /// existing session and consulted nothing but "does this user still exist" (issue #58).
    /// </para>
    /// <para>
    /// <b>What that door was worth.</b> Suspension writes the state and revokes the sessions as two
    /// separate operations in <c>AccountSuspendingHandler</c> — <c>Suspend()</c> and
    /// <c>SaveChangesAsync</c> first, <c>RevokeAllAsync</c> after. Anything that stops the second
    /// from happening (a cancelled request, a failure in the session store, a process that dies in
    /// between) leaves a suspended login holding live sessions, and refresh would then keep issuing
    /// fresh access tokens from them indefinitely, each rotation extending the next. Asking the state
    /// directly removes the dependency on that revocation being complete and timely.
    /// </para>
    /// <para>
    /// <b>This still is not a structural guarantee</b>, and <c>AuthenticationCompleter</c>'s remarks
    /// say the same about itself: <c>IAccessTokenIssuer</c> is an ordinary service any handler can
    /// inject. What this member buys is that the question has ONE definition, so a new door is a
    /// missing call to a named member rather than a missing comparison somebody has to notice.
    /// <c>maran structure</c>'s check 26 is what turns that into a gate.
    /// </para>
    /// </remarks>
    public bool MayAuthenticate
    {
        get { return Lifecycle == UserLifecycle.Active; }
    }

    /// <summary>
    /// Whether the owner has yet to accept the invitation and choose a password — in either of the
    /// two lifecycles that hold no password, whether or not the account is suspended meanwhile.
    /// </summary>
    public bool HasPendingInvitation
    {
        get { return Lifecycle is UserLifecycle.Invited or UserLifecycle.InvitedAccountSuspended; }
    }

    /// <summary>
    /// The hosting account this user was created for, for a <see cref="UserRole.Customer"/>; null for
    /// an administrator, who owns none and reaches all of them.
    /// </summary>
    /// <remarks>
    /// <b>No longer the truth about which accounts a login may act for.</b> That is the login's
    /// <see cref="AccountMembership"/> rows, which can name several accounts. This column is kept for
    /// one release because the previous release reads it (rules/architecture.md, expand then
    /// contract); it is written when a customer login is created and cleared when its account is
    /// deleted, and nothing in this release decides access from it. Its unique index constrains only
    /// this column, so a second membership never violates it. The next release drops both.
    /// </remarks>
    public Guid? AccountId { get; private set; }

    /// <summary>
    /// The TOTP shared secret, base32-encoded and encrypted at rest; null when two-factor is not
    /// enrolled. Its presence is the single fact "a second factor is enrolled": <see cref="IsTotpEnabled"/>
    /// restates it and nothing is stored beside it that could disagree.
    /// </summary>
    public string? TotpSecret { get; private set; }

    /// <summary>
    /// Whether a second factor is enrolled, which is exactly whether <see cref="TotpSecret"/> exists.
    /// Derived on every read, never stored by this release.
    /// </summary>
    public bool IsTotpEnabled
    {
        get { return TotpSecret is not null; }
    }

    /// <summary>
    /// The previous release's enrolment flag, stored in <c>Users."IsTotpEnabled"</c>. <b>Legacy:
    /// written only by <see cref="EnableTotp"/> and <see cref="DisableTotp"/> and never read by this
    /// release's code</b>; <see cref="TotpSecret"/> is the truth. Kept for one release because the
    /// previous release reads the column (rules/architecture.md, expand then contract).
    /// </summary>
    public bool LegacyTotpEnabled { get; private set; }

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

    /// <summary>Creates the login of a hosting account's owner, before they have set a password.</summary>
    /// <param name="id">The login's identity.</param>
    /// <param name="username">The account's system user name, which is also the login name.</param>
    /// <param name="email">The address the invitation was sent to.</param>
    /// <param name="accountId">The hosting account this login owns.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    /// <returns>A login in <see cref="UserLifecycle.Invited"/>, which cannot sign in.</returns>
    public static User Invite(Guid id, string username, string email, Guid accountId, DateTimeOffset createdAt)
    {
        var user = InviteMember(id, username, email, createdAt);

        user.AssignAccount(accountId);
        return user;
    }

    /// <summary>
    /// Creates the login of a person an account's owner added to the account, before they have set a
    /// password. It owns no account: what it may reach is stated by its <c>AccountMembership</c> rows.
    /// </summary>
    /// <param name="id">The login's identity.</param>
    /// <param name="username">The login name the owner chose.</param>
    /// <param name="email">The address the invitation is sent to.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    /// <returns>A login in <see cref="UserLifecycle.Invited"/>, which cannot sign in.</returns>
    public static User InviteMember(Guid id, string username, string email, DateTimeOffset createdAt)
    {
        var user = new User(id, username, email, string.Empty, UserRole.Customer, createdAt)
        {
            Lifecycle = UserLifecycle.Invited,
        };

        user.SyncLegacyColumns();
        return user;
    }

    /// <summary>
    /// Accepts an invitation: stores the first password and makes the login usable — unless the
    /// account was suspended while the invitation was outstanding, in which case the password is
    /// stored but the login comes out <see cref="UserLifecycle.Suspended"/> rather than usable.
    /// </summary>
    /// <param name="passwordHash">The Argon2id hash of the password its owner chose.</param>
    /// <remarks>
    /// A customer must never be able to turn a suspended hosting account into a working login simply
    /// by finding an old invitation link. Storing the password rather than refusing it means the
    /// owner does not have to accept the invitation a second time once the account is resumed —
    /// <see cref="Resume"/> then returns them straight to <see cref="UserLifecycle.Active"/> with the
    /// password they already chose. A login that already holds a password keeps its lifecycle.
    /// </remarks>
    public void Activate(string passwordHash)
    {
        PasswordHash = passwordHash;

        switch (Lifecycle)
        {
            case UserLifecycle.Invited:
                Lifecycle = UserLifecycle.Active;
                break;
            case UserLifecycle.InvitedAccountSuspended:
                Lifecycle = UserLifecycle.Suspended;
                break;
            case UserLifecycle.Active:
            case UserLifecycle.Suspended:
                break;
            default:
                throw new InvalidOperationException($"Unknown user lifecycle {Lifecycle}.");
        }

        SyncLegacyColumns();
    }

    /// <summary>
    /// Blocks sign-in because the hosting account was suspended. Keeps the password.
    /// </summary>
    /// <remarks>
    /// A login that has not been claimed yet moves to <see cref="UserLifecycle.InvitedAccountSuspended"/>
    /// rather than <see cref="UserLifecycle.Suspended"/>: suspending an account whose owner never set
    /// a password must not silently consume the invitation they have not used yet, and the lifecycle
    /// remembers the suspension so <see cref="Activate"/> keeps an invitation accepted during it from
    /// producing a usable login. A login already suspended stays so.
    /// </remarks>
    public void Suspend()
    {
        switch (Lifecycle)
        {
            case UserLifecycle.Active:
                Lifecycle = UserLifecycle.Suspended;
                break;
            case UserLifecycle.Invited:
                Lifecycle = UserLifecycle.InvitedAccountSuspended;
                break;
            case UserLifecycle.InvitedAccountSuspended:
            case UserLifecycle.Suspended:
                break;
            default:
                throw new InvalidOperationException($"Unknown user lifecycle {Lifecycle}.");
        }

        SyncLegacyColumns();
    }

    /// <summary>Lifts a suspension, returning the login to the lifecycle it was in before.</summary>
    public void Resume()
    {
        switch (Lifecycle)
        {
            case UserLifecycle.Suspended:
                Lifecycle = UserLifecycle.Active;
                break;
            case UserLifecycle.InvitedAccountSuspended:
                Lifecycle = UserLifecycle.Invited;
                break;
            case UserLifecycle.Active:
            case UserLifecycle.Invited:
                break;
            default:
                throw new InvalidOperationException($"Unknown user lifecycle {Lifecycle}.");
        }

        SyncLegacyColumns();
    }

    /// <summary>Binds this user to the hosting account they own.</summary>
    /// <param name="accountId">The owned account's identity.</param>
    public void AssignAccount(Guid accountId)
    {
        AccountId = accountId;
    }

    /// <summary>
    /// Forgets the older single-account pointer when it names <paramref name="accountId"/>, which is
    /// being deleted. A no-op for any other value.
    /// </summary>
    /// <param name="accountId">The account that is going away.</param>
    /// <remarks>
    /// Only the pointer is cleared: which accounts the login may act for is its memberships
    /// (<see cref="AccountMembership"/>), and those are removed by the deletion handler itself.
    /// </remarks>
    public void UnassignAccount(Guid accountId)
    {
        if (AccountId == accountId)
        {
            AccountId = null;
        }
    }

    /// <summary>
    /// Replaces the login's address — the one its invitation and its password resets are sent to.
    /// </summary>
    /// <param name="email">The new, validated address.</param>
    public void ChangeEmail(string email)
    {
        Email = email;
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
        LegacyTotpEnabled = true;
    }

    /// <summary>
    /// Turns two-factor authentication off. Clears the secret rather than only the flag: a disabled
    /// flag sitting beside a live secret is one accidental write away from being enabled again with
    /// a factor the user believes they have removed.
    /// </summary>
    public void DisableTotp()
    {
        TotpSecret = null;
        LegacyTotpEnabled = false;
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

    /// <summary>
    /// Restates <see cref="Lifecycle"/> into the two legacy columns the previous release reads, so a
    /// rollback finds the row it would have written itself. Called by every method that changes the
    /// lifecycle and by nothing else; deleted with the columns in the contract release.
    /// </summary>
    private void SyncLegacyColumns()
    {
        (LegacyState, LegacyAccountSuspended) = Lifecycle switch
        {
            UserLifecycle.Active => (UserState.Active, false),
            UserLifecycle.Invited => (UserState.Invited, false),
            UserLifecycle.InvitedAccountSuspended => (UserState.Invited, true),
            UserLifecycle.Suspended => (UserState.Suspended, true),
            _ => throw new InvalidOperationException($"Unknown user lifecycle {Lifecycle}."),
        };
    }
}
