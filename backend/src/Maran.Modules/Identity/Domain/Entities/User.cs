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

    /// <summary>
    /// Whether the password this login holds was chosen by somebody else and must be replaced before the
    /// login can do anything else. Set when an administrator creates the account with an initial
    /// password (<see cref="CreateWithPassword"/>) and cleared only by <see cref="ChangePassword"/> — a
    /// completed change or a completed reset. While it is true the access token carries
    /// <c>PanelClaimTypes.PasswordChangeRequired</c> and every endpoint but the password change refuses it.
    /// </summary>
    public bool PasswordChangeRequired { get; private set; }

    /// <summary>What this user is allowed to reach.</summary>
    public UserRole Role { get; private set; }

    /// <summary>
    /// The reseller this login is the one login of, or null for every login that is not a reseller's.
    /// Set exactly once, by <see cref="InviteReseller"/>; a login's reseller never changes. A plain
    /// identifier with no foreign key: the reseller row lives in the Accounts module's schema, which
    /// this module does not read (rules/architecture.md), so the link is the id the Accounts module
    /// announced in <c>ResellerCreated</c> and the database holds at most one login per reseller
    /// (<c>UX_Users_ResellerId</c>).
    /// </summary>
    public Guid? ResellerId { get; private set; }

    /// <summary>
    /// Whether this login itself has been suspended, which only a reseller's login can be. A
    /// customer's suspension belongs to one hosting account and is stored on the membership
    /// (<see cref="AccountMembership.State"/>); a reseller holds no membership, so its suspension is
    /// stored here. Separate from <see cref="Lifecycle"/> because the two are independent: a reseller
    /// can be suspended before it accepted its invitation and must still be invited afterwards, and
    /// accepting the invitation must not lift the suspension. Changed only by <see cref="Suspend"/>
    /// and <see cref="Resume"/>.
    /// </summary>
    public bool IsSuspended { get; private set; }

    /// <summary>
    /// Which of the two situations this login is in: usable, or waiting for its owner to set a
    /// password. The one stored fact about whether the login itself may be used. An invited login
    /// holds no password at all rather than an empty hash: a sentinel hash in the same field a real
    /// one lives in is one careless comparison away from being accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whether the login may act for a particular hosting account is not decided here: that is the
    /// <see cref="AccountMembership.State"/> of the membership, because a suspension belongs to one
    /// account and a login can hold several.
    /// </para>
    /// <para>
    /// Replaces the pair <see cref="LegacyState"/> and <see cref="LegacyAccountSuspended"/>. Every
    /// mutator below writes this property and then restates it into that pair, together with the
    /// owner membership's suspension (<see cref="SyncLegacyColumns"/>), so the legacy columns never
    /// disagree with what this release stores until the contract release drops them.
    /// </para>
    /// </remarks>
    public UserLifecycle Lifecycle { get; private set; }

    /// <summary>
    /// The previous release's coarse state, stored in <c>Users."State"</c>. <b>Legacy: written only
    /// by <see cref="SyncLegacyColumns"/> and the lifecycle mutators, and never read by this release's
    /// code</b>; read <see cref="Lifecycle"/> instead. It is <see cref="UserState.Suspended"/> for a
    /// claimed login whose owned account is suspended, because the previous release reads that to
    /// refuse a sign-in. Kept for one release because the previous release reads the column
    /// (rules/architecture.md, expand then contract); the next release drops it.
    /// </summary>
    public UserState LegacyState { get; private set; }

    /// <summary>
    /// The previous release's "this login's account is suspended" flag, stored in
    /// <c>Users."IsAccountSuspended"</c>. <b>Legacy: written only by <see cref="SyncLegacyColumns"/>
    /// and never read by this release's code</b>: it restates whether the OWNER membership of the
    /// account this login owns is <see cref="MembershipState.Suspended"/>, so a rollback to the
    /// previous release still sees the suspension. The truth is the membership
    /// (<see cref="AccountMembership.State"/>), not the login. Dropped by the same later release as
    /// <see cref="LegacyState"/>.
    /// </summary>
    public bool LegacyAccountSuspended { get; private set; }

    /// <summary>
    /// Whether this login itself may be used to obtain a token — the one place that question is
    /// asked, so a new way of minting one cannot be added without asking it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a member here rather than an <c>if</c> in each handler.</b> There were two sign-in
    /// paths, each with its own copy of this comparison, and the two-factor path's copy was missing.
    /// That was fixed by routing both through <c>AuthenticationCompleter</c>, and the comparison then
    /// existed once. It was not the only door: <c>RefreshSessionCommandHandler</c> mints an access
    /// token from an existing session and consulted nothing but "does this user still exist"
    /// (issue #58).
    /// </para>
    /// <para>
    /// <b>This answers about the LOGIN only.</b> Whether the login may act for a particular hosting
    /// account is the <see cref="AccountMembership.State"/> of that membership, asked of the
    /// memberships by <c>ActiveAccountResolver</c> wherever a token is minted. The two questions are
    /// separate because a person with two accounts is one login: suspending one account must not
    /// make this property false. It is false for a reseller's login while that login
    /// <see cref="IsSuspended"/>, because a reseller has no membership to say it.
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
        get { return Lifecycle == UserLifecycle.Active && !IsSuspended; }
    }

    /// <summary>
    /// Whether the person has yet to accept the invitation and choose a password.
    /// </summary>
    public bool HasPendingInvitation
    {
        get { return Lifecycle == UserLifecycle.Invited; }
    }

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

    /// <summary>
    /// Creates the login of a person invited to the panel, before they have set a password. It owns
    /// and reaches no account by itself: what it may act for is stated by its
    /// <see cref="AccountMembership"/> rows, which the caller creates beside it.
    /// </summary>
    /// <param name="id">The login's identity.</param>
    /// <param name="username">
    /// The login name: the account's system user name for an owner, the name the owner chose for a member.
    /// </param>
    /// <param name="email">The address the invitation is sent to.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    /// <returns>A login in <see cref="UserLifecycle.Invited"/>, which cannot sign in.</returns>
    public static User Invite(Guid id, string username, string email, DateTimeOffset createdAt)
    {
        var user = new User(id, username, email, string.Empty, UserRole.Customer, createdAt)
        {
            Lifecycle = UserLifecycle.Invited,
        };

        user.RestateLegacyColumns();
        return user;
    }

    /// <summary>
    /// Creates the login of a customer whose first password was chosen by whoever created the account,
    /// and which the customer must replace at their first sign-in. It owns and reaches no account by
    /// itself: what it may act for is stated by its <see cref="AccountMembership"/> rows, which the
    /// caller creates beside it.
    /// </summary>
    /// <param name="id">The login's identity.</param>
    /// <param name="username">The login name: the account's system user name.</param>
    /// <param name="email">The owner's contact address.</param>
    /// <param name="passwordHash">The Argon2id hash of the initial password, produced by <see cref="IPasswordHasher"/>.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    /// <returns>
    /// A login in <see cref="UserLifecycle.Active"/> with <see cref="PasswordChangeRequired"/> set: it can
    /// sign in, and can then do nothing but change its password.
    /// </returns>
    public static User CreateWithPassword(
        Guid id,
        string username,
        string email,
        string passwordHash,
        DateTimeOffset createdAt)
    {
        var user = new User(id, username, email, passwordHash, UserRole.Customer, createdAt)
        {
            Lifecycle = UserLifecycle.Active,
            PasswordChangeRequired = true,
        };

        user.RestateLegacyColumns();
        return user;
    }

    /// <summary>
    /// Creates the login of a reseller, before its contact has set a password. It is the only login
    /// the reseller has and it owns no hosting account: what the reseller may do is stated by its
    /// role and by the reseller row in the Accounts module.
    /// </summary>
    /// <param name="id">The login's identity.</param>
    /// <param name="username">The login name: the reseller's name.</param>
    /// <param name="email">The address the invitation is sent to: the reseller's contact address.</param>
    /// <param name="resellerId">The reseller this login is the one login of.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    /// <returns>A login in <see cref="UserLifecycle.Invited"/>, which cannot sign in.</returns>
    public static User InviteReseller(Guid id, string username, string email, Guid resellerId, DateTimeOffset createdAt)
    {
        var user = new User(id, username, email, string.Empty, UserRole.Reseller, createdAt)
        {
            Lifecycle = UserLifecycle.Invited,
            ResellerId = resellerId,
        };

        user.RestateLegacyColumns();
        return user;
    }

    /// <summary>
    /// Accepts an invitation: stores the first password and makes the login usable. A login that
    /// already holds a password keeps its lifecycle.
    /// </summary>
    /// <param name="passwordHash">The Argon2id hash of the password its owner chose.</param>
    /// <remarks>
    /// Accepting an invitation never lifts a suspension: if the account was suspended while the
    /// invitation was outstanding, the login becomes usable but its membership of that account is
    /// still <see cref="MembershipState.Suspended"/>, so sign-in refuses it until the account is
    /// resumed. The password is stored regardless, so the owner does not have to accept the invitation
    /// a second time.
    /// </remarks>
    public void Activate(string passwordHash)
    {
        PasswordHash = passwordHash;
        Lifecycle = UserLifecycle.Active;
        RestateLegacyColumns();
    }

    /// <summary>
    /// Suspends a reseller's login: it can no longer be used to obtain a token. Repeating it changes
    /// nothing. The sessions it already holds are ended by the caller, because a suspension that only
    /// blocks the next sign-in leaves whoever is signed in working until their token expires.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The login is not a reseller's. A customer is suspended per account, on the membership, and an
    /// administrator is never suspended; reaching this for either is a defect in the caller, and
    /// silently suspending the login would block every account the person holds.
    /// </exception>
    public void Suspend()
    {
        RequireResellerLogin();
        IsSuspended = true;
    }

    /// <summary>
    /// Lifts the suspension of a reseller's login. Repeating it changes nothing. It restores the
    /// ability to sign in and nothing else: the sessions the suspension ended stay ended.
    /// </summary>
    /// <exception cref="InvalidOperationException">The login is not a reseller's; see <see cref="Suspend"/>.</exception>
    public void Resume()
    {
        RequireResellerLogin();
        IsSuspended = false;
    }

    /// <summary>
    /// Replaces the login's address — the one its invitation and its password resets are sent to.
    /// </summary>
    /// <param name="email">The new, validated address.</param>
    public void ChangeEmail(string email)
    {
        Email = email;
    }

    /// <summary>
    /// Replaces the login name. Only the one login a reseller has is ever renamed, when the reseller
    /// is: the reseller's name is its login name.
    /// </summary>
    /// <param name="username">The new, validated login name.</param>
    public void Rename(string username)
    {
        Username = username;
    }

    /// <summary>
    /// Replaces the password with one its owner just chose, and ends the obligation to do so
    /// (<see cref="PasswordChangeRequired"/>). Used by the signed-in change and by the reset by mail: in
    /// both the person holding the login picked the new password, which is what the obligation asks for.
    /// </summary>
    /// <param name="passwordHash">The new Argon2id hash.</param>
    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        PasswordChangeRequired = false;
    }

    /// <summary>
    /// Re-derives the stored hash of the SAME password with stronger parameters. It changes how the
    /// password is stored, not the password, so it leaves <see cref="PasswordChangeRequired"/> alone: a
    /// sign-in with a still-unreplaced initial password must not clear the obligation by being rehashed.
    /// </summary>
    /// <param name="passwordHash">The new Argon2id hash of the unchanged password.</param>
    public void Rehash(string passwordHash)
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
    /// Restates the suspension of the account this login OWNS into the two legacy columns the
    /// previous release reads, so a rollback still refuses to sign in the owner of a suspended
    /// account. Called by the suspension and reactivation handlers with the state of the owner
    /// membership, and by nothing else; deleted with the columns in the contract release.
    /// </summary>
    /// <param name="ownerMembershipState">The state of this login's owner membership of the account.</param>
    public void SyncLegacyColumns(MembershipState ownerMembershipState)
    {
        LegacyAccountSuspended = ownerMembershipState == MembershipState.Suspended;
        RestateLegacyColumns();
    }

    /// <summary>Refuses a suspension change on a login that is not a reseller's.</summary>
    /// <exception cref="InvalidOperationException">The login is not a reseller's.</exception>
    private void RequireResellerLogin()
    {
        if (Role != UserRole.Reseller)
        {
            throw new InvalidOperationException("Only a reseller's login is suspended on the login itself.");
        }
    }

    /// <summary>
    /// Restates <see cref="Lifecycle"/> and the stored suspension flag into the legacy
    /// <see cref="LegacyState"/>, the way the previous release wrote it: a claimed login of a
    /// suspended account reads <see cref="UserState.Suspended"/>, an invited one stays
    /// <see cref="UserState.Invited"/> with the flag set. Called by every method that changes the
    /// lifecycle, so accepting an invitation never lifts a suspension the flag records.
    /// </summary>
    private void RestateLegacyColumns()
    {
        LegacyState = Lifecycle switch
        {
            UserLifecycle.Active => LegacyAccountSuspended ? UserState.Suspended : UserState.Active,
            UserLifecycle.Invited => UserState.Invited,
            _ => throw new InvalidOperationException($"Unknown user lifecycle {Lifecycle}."),
        };
    }
}
