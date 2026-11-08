using System.ComponentModel.DataAnnotations;

namespace Maran.Host.Configuration;

/// <summary>
/// Tuning for the panel's rate limiters. Bound from the <c>RateLimiting</c> configuration
/// section and validated at startup so a nonsensical limit (zero permits, a negative window)
/// fails the boot instead of silently disabling protection.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Sign-in attempts allowed from one client address within <see cref="LoginLockoutSeconds"/>, counted
    /// in segments of <see cref="LoginWindowSeconds"/> (<c>LoginRateLimitPolicy</c>).
    /// </summary>
    [Range(1, 100)]
    public int LoginMaxAttempts { get; set; } = 5;

    /// <summary>
    /// Length of one segment of the login limiter's sliding window, in seconds: permits used in a segment
    /// return once that segment slides out of the <see cref="LoginLockoutSeconds"/> window.
    /// </summary>
    [Range(1, 3600)]
    public int LoginWindowSeconds { get; set; } = 60;

    /// <summary>
    /// How long one client address is locked out once <see cref="LoginMaxAttempts"/> is exceeded, in
    /// seconds. Enforced by giving the limiter a replenishment window at least this
    /// long, so a blocked caller cannot regain permits before the lockout elapses.
    /// </summary>
    [Range(1, 86_400)]
    public int LoginLockoutSeconds { get; set; } = 300;

    /// <summary>Password-reset requests allowed from one address within <see cref="PasswordResetWindowSeconds"/>.</summary>
    /// <remarks>
    /// Small on purpose. A person who has forgotten their password asks once, looks at their mail,
    /// and perhaps asks again; three in the window covers that with room to spare. Every request
    /// above it is somebody using the panel to send mail to an address they chose.
    /// </remarks>
    [Range(1, 100)]
    public int PasswordResetMaxRequests { get; set; } = 3;

    /// <summary>Length of the sliding window password-reset requests are counted over, in seconds.</summary>
    [Range(1, 86_400)]
    public int PasswordResetWindowSeconds { get; set; } = 900;

    /// <summary>Invitation-acceptance attempts allowed from one address within <see cref="InvitationWindowSeconds"/>.</summary>
    /// <remarks>
    /// A separate number from <see cref="PasswordResetMaxRequests"/> on purpose (see
    /// <c>RateLimitPolicies.Invitation</c>): a fresh customer setting a first password does not have
    /// a mail-bomb hazard to bound the way an anonymous reset request does, so there is room to be
    /// more forgiving of a mistyped password confirmation without that generosity leaking into the
    /// reset endpoint's much tighter budget.
    /// </remarks>
    [Range(1, 100)]
    public int InvitationMaxRequests { get; set; } = 10;

    /// <summary>Length of the window invitation-acceptance attempts are counted over, in seconds.</summary>
    [Range(1, 86_400)]
    public int InvitationWindowSeconds { get; set; } = 900;

    /// <summary>Invitations one hosting account may send within <see cref="InvitationSendWindowSeconds"/>.</summary>
    /// <remarks>
    /// Its own number and its own bucket (<c>RateLimitPolicies.InvitationSending</c>), distinct from
    /// <see cref="InvitationMaxRequests"/>, which bounds acceptance. Every permitted request makes
    /// the panel mail an address the owner typed, so the figure is small: twenty a window is a whole
    /// team being onboarded at once, and anything beyond is somebody using the panel to send mail.
    /// </remarks>
    [Range(1, 1_000)]
    public int InvitationSendMaxRequests { get; set; } = 20;

    /// <summary>Length of the window invitation sending is counted over, in seconds.</summary>
    [Range(1, 86_400)]
    public int InvitationSendWindowSeconds { get; set; } = 3600;

    /// <summary>Requests allowed per authenticated account (or IP, when anonymous) within <see cref="ApiWindowSeconds"/>.</summary>
    [Range(1, 100_000)]
    public int ApiPermitLimit { get; set; } = 300;

    /// <summary>Length of the fixed window the API request count is measured over, in seconds.</summary>
    [Range(1, 3600)]
    public int ApiWindowSeconds { get; set; } = 60;

    /// <summary>
    /// How many site-log tails ONE account may hold open at the same time.
    /// </summary>
    /// <remarks>
    /// A concurrency budget, not a rate: see <c>SiteLogStreamRateLimitPolicy</c>. Six is a generous
    /// number of log panes for one customer to have open and a small fraction of the root daemon's
    /// blocking-thread pool, which is what an open tail actually consumes.
    /// </remarks>
    [Range(1, 1_000)]
    public int SiteLogConcurrentStreamLimit { get; set; } = 6;

    /// <summary>
    /// Restores allowed for one hosting account within <see cref="BackupRestoreWindowSeconds"/>.
    /// </summary>
    /// <remarks>
    /// Three, and deliberately small: a restore is a thing a person does on purpose, not something a
    /// screen does on their behalf, and a fourth attempt inside an hour is more likely to be a
    /// mistake being repeated than a need.
    /// </remarks>
    public int BackupRestoreMaxRequests { get; set; } = 3;

    /// <summary>The window <see cref="BackupRestoreMaxRequests"/> is counted over, in seconds.</summary>
    public int BackupRestoreWindowSeconds { get; set; } = 3600;

    /// <summary>
    /// On-request backup verifications allowed for one hosting account within
    /// <see cref="BackupVerifyWindowSeconds"/>.
    /// </summary>
    /// <remarks>
    /// Ten: each one makes the root agent read a whole archive and hash it, so the number bounds disk
    /// I/O the account can cause, and ten in an hour covers checking a month of dailies once. The
    /// verification the panel runs itself after every backup does not pass through this limiter.
    /// </remarks>
    [Range(1, 1_000)]
    public int BackupVerifyMaxRequests { get; set; } = 10;

    /// <summary>The window <see cref="BackupVerifyMaxRequests"/> is counted over, in seconds.</summary>
    [Range(1, 86_400)]
    public int BackupVerifyWindowSeconds { get; set; } = 3600;

    /// <summary>
    /// DNS requests allowed for one hosting account within <see cref="DnsWindowSeconds"/>, reads and edits
    /// together.
    /// </summary>
    /// <remarks>
    /// Sixty a minute: an accepted edit makes the root agent re-render a whole zone and reload it in the name
    /// server, which is the cost this bounds, while a person editing records by hand stays far below it.
    /// </remarks>
    [Range(1, 10_000)]
    public int DnsMaxRequests { get; set; } = 60;

    /// <summary>The window <see cref="DnsMaxRequests"/> is counted over, in seconds.</summary>
    [Range(1, 86_400)]
    public int DnsWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Journal exports allowed for one signed-in administrator within <see cref="AuditExportWindowSeconds"/>,
    /// counted across the audit export and the personal-data export together.
    /// </summary>
    /// <remarks>
    /// Six: one request hands the caller a large part of the security record, so the number bounds how
    /// fast a stolen administrator session can take it out, and six in an hour covers a person pulling
    /// a few windows for a review. It slows that session and does not stop it.
    /// </remarks>
    [Range(1, 1_000)]
    public int AuditExportMaxRequests { get; set; } = 6;

    /// <summary>The window <see cref="AuditExportMaxRequests"/> is counted over, in seconds.</summary>
    [Range(1, 86_400)]
    public int AuditExportWindowSeconds { get; set; } = 3600;

    /// <summary>
    /// Global-search requests allowed for one signed-in user within <see cref="SearchWindowSeconds"/>
    /// (<c>SearchRateLimitPolicy</c>).
    /// </summary>
    /// <remarks>
    /// One hundred and twenty a minute: the palette sends a request per pause in typing, a fast typist
    /// pauses perhaps twice a second at most, and each request reads several modules' tables, so the
    /// number is generous for a person and a wall for a loop enumerating the namespace.
    /// </remarks>
    [Range(1, 10_000)]
    public int SearchMaxRequests { get; set; } = 120;

    /// <summary>The window <see cref="SearchMaxRequests"/> is counted over, in seconds.</summary>
    [Range(1, 3600)]
    public int SearchWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Provisioning writes allowed for one API key within <see cref="ProvisioningWindowSeconds"/>
    /// (<c>ProvisioningRateLimitPolicy</c>).
    /// </summary>
    /// <remarks>
    /// Sixty a minute: a billing system provisioning accounts in a burst needs room, every permitted
    /// request creates, changes or deletes a hosting account, and a loop with a stolen key is held to
    /// one a second. It slows that key and does not stop it; revocation does.
    /// </remarks>
    [Range(1, 100_000)]
    public int ProvisioningMaxRequests { get; set; } = 60;

    /// <summary>The window <see cref="ProvisioningMaxRequests"/> is counted over, in seconds.</summary>
    [Range(1, 3600)]
    public int ProvisioningWindowSeconds { get; set; } = 60;

    /// <summary>
    /// File-manager requests allowed for one hosting account within <see cref="FilesWindowSeconds"/>
    /// (<c>FilesRateLimitPolicy</c>).
    /// </summary>
    /// <remarks>
    /// Six hundred a minute: a file manager sends a request per folder opened and per search pause, so a
    /// busy person needs room, and a loop listing folders is held to ten a second. Transfers are bounded
    /// separately, by their own concurrency slots, because a request count says nothing about how long
    /// a stream is held open.
    /// </remarks>
    [Range(1, 100_000)]
    public int FilesMaxRequests { get; set; } = 600;

    /// <summary>The window <see cref="FilesMaxRequests"/> is counted over, in seconds.</summary>
    [Range(1, 3600)]
    public int FilesWindowSeconds { get; set; } = 60;
}
