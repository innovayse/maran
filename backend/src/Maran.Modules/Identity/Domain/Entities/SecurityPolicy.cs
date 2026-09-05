namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// The panel's operator-configurable security policy: at most one row, ever (R12). It is the single
/// answer to "how long must a password be", "must an administrator hold a second factor", and "how
/// many wrong passwords lock an account".
/// </summary>
/// <remarks>
/// <para>
/// <b>A singleton by construction, not by convention.</b> The primary key is
/// <see cref="SingletonId"/> and no code anywhere generates a different one, so a second row cannot
/// be inserted: the database refuses it. Two rows would mean two answers to every question above,
/// and whichever the reader happened to load would be the one that took effect.
/// </para>
/// <para>
/// <b>The defaults live here as constants, and they are the same values on a fresh installation and
/// after a cache miss.</b> There is deliberately no startup seeder writing this row. A seeder would
/// be a second place the defaults are stated, and — worse — a panel whose seeder had not run yet
/// would have no policy at all, which is a window in which the password rule is whatever the
/// validator falls back to. Instead the absence of the row IS the defaults (see
/// <c>SecurityPolicySnapshot.Default</c>), and the first save materialises it.
/// </para>
/// <para>
/// <b>Brute-force counting is deliberately NOT here.</b> Its threshold and window are
/// <c>BruteForceOptions</c>, bound from configuration and read by <c>BruteForceDetector</c>. Adding
/// the same two numbers to this row without moving the reader would give the panel two sources for
/// one decision, and the screen would show a value nothing obeyed. Unifying them is a follow-up
/// named in this task's report, not a field added here on speculation.
/// </para>
/// </remarks>
public sealed class SecurityPolicy
{
    /// <summary>The one primary key this table ever holds.</summary>
    /// <remarks>
    /// A fixed value rather than a generated one so that "insert if missing, otherwise update" is a
    /// single primary-key lookup with no ordering, no <c>FirstOrDefault</c> over an unordered table,
    /// and no window in which two concurrent saves each create a row.
    /// </remarks>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000005350");

    /// <summary>
    /// The shortest password a fresh panel accepts. Length, and not being the username, is the whole
    /// policy: character-class requirements mostly produce "Password1!" and a note on a monitor.
    /// </summary>
    public const int DefaultMinimumPasswordLength = 12;

    /// <summary>
    /// Whether a fresh panel forces administrators to enrol a second factor. Off by default: turning
    /// it on locks every administrator into the enrolment screen until they finish, which is the
    /// right behaviour once an operator asks for it and a hostile surprise if it is the default.
    /// </summary>
    public const bool DefaultForceTwoFactorForAdmins = false;

    /// <summary>
    /// Consecutive failures that lock an account on a fresh panel. Deliberately larger than the
    /// per-address rate limit: this is the last line, for an attacker with enough addresses to walk
    /// past the first one, and it must not fire on somebody who simply mistyped a few times.
    /// </summary>
    public const int DefaultMaxFailedLoginAttempts = 10;

    /// <summary>How long a locked account stays locked on a fresh panel, in minutes.</summary>
    public const int DefaultLockoutMinutes = 15;

    /// <summary>The row's identity; always <see cref="SingletonId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The shortest password the panel accepts.</summary>
    public int MinimumPasswordLength { get; private set; }

    /// <summary>Whether an administrator without a second factor is steered into enrolment.</summary>
    public bool ForceTwoFactorForAdmins { get; private set; }

    /// <summary>Consecutive failed sign-ins that lock an account.</summary>
    public int MaxFailedLoginAttempts { get; private set; }

    /// <summary>How long a locked account stays locked, in minutes.</summary>
    public int LockoutMinutes { get; private set; }

    /// <summary>When the policy was last saved.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates the panel's security policy for the first time.</summary>
    /// <param name="minimumPasswordLength">The shortest password the panel accepts.</param>
    /// <param name="forceTwoFactorForAdmins">Whether administrators must hold a second factor.</param>
    /// <param name="maxFailedLoginAttempts">Consecutive failed sign-ins that lock an account.</param>
    /// <param name="lockoutMinutes">How long a locked account stays locked, in minutes.</param>
    /// <param name="updatedAt">When the policy was saved, from the panel's clock.</param>
    public SecurityPolicy(
        int minimumPasswordLength,
        bool forceTwoFactorForAdmins,
        int maxFailedLoginAttempts,
        int lockoutMinutes,
        DateTimeOffset updatedAt)
    {
        Id = SingletonId;
        MinimumPasswordLength = minimumPasswordLength;
        ForceTwoFactorForAdmins = forceTwoFactorForAdmins;
        MaxFailedLoginAttempts = maxFailedLoginAttempts;
        LockoutMinutes = lockoutMinutes;
        UpdatedAt = updatedAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SecurityPolicy()
    {
    }

    /// <summary>Replaces every setting with the values just saved.</summary>
    /// <param name="minimumPasswordLength">The shortest password the panel accepts.</param>
    /// <param name="forceTwoFactorForAdmins">Whether administrators must hold a second factor.</param>
    /// <param name="maxFailedLoginAttempts">Consecutive failed sign-ins that lock an account.</param>
    /// <param name="lockoutMinutes">How long a locked account stays locked, in minutes.</param>
    /// <param name="updatedAt">When the policy was saved, from the panel's clock.</param>
    /// <remarks>
    /// Whole-row replacement rather than a method per field: the policy is edited on one screen that
    /// submits all of it, and a per-field setter would let a caller assign a value the validator
    /// never saw (rules/csharp.md "Domain models are rich").
    /// </remarks>
    public void Replace(
        int minimumPasswordLength,
        bool forceTwoFactorForAdmins,
        int maxFailedLoginAttempts,
        int lockoutMinutes,
        DateTimeOffset updatedAt)
    {
        MinimumPasswordLength = minimumPasswordLength;
        ForceTwoFactorForAdmins = forceTwoFactorForAdmins;
        MaxFailedLoginAttempts = maxFailedLoginAttempts;
        LockoutMinutes = lockoutMinutes;
        UpdatedAt = updatedAt;
    }
}
