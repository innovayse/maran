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

    /// <summary>Failed login attempts allowed per (IP, username) partition within <see cref="LoginWindowSeconds"/>.</summary>
    [Range(1, 100)]
    public int LoginMaxAttempts { get; set; } = 5;

    /// <summary>Length of the sliding window the login attempt count is measured over, in seconds.</summary>
    [Range(1, 3600)]
    public int LoginWindowSeconds { get; set; } = 60;

    /// <summary>
    /// How long a (IP, username) partition is locked out once <see cref="LoginMaxAttempts"/> is
    /// exceeded, in seconds. Enforced by giving the limiter a replenishment window at least this
    /// long, so a blocked caller cannot regain permits before the lockout elapses.
    /// </summary>
    [Range(1, 86_400)]
    public int LoginLockoutSeconds { get; set; } = 300;

    /// <summary>Requests allowed per authenticated account (or IP, when anonymous) within <see cref="ApiWindowSeconds"/>.</summary>
    [Range(1, 100_000)]
    public int ApiPermitLimit { get; set; } = 300;

    /// <summary>Length of the fixed window the API request count is measured over, in seconds.</summary>
    [Range(1, 3600)]
    public int ApiWindowSeconds { get; set; } = 60;
}
