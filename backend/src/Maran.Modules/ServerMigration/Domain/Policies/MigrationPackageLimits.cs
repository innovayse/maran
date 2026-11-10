namespace Maran.Modules.ServerMigration.Domain.Policies;

/// <summary>
/// The fixed bounds of moving an account between servers (issue #127): what the strict package reader will
/// accept, and how long an export and a pull grant live.
/// </summary>
/// <remarks>
/// Constants, not configuration: a bound that an operator can raise is a bound an attacker who reaches the
/// configuration can remove, and no variable is read for any of these. The agent applies its own ceiling to the
/// artifact as well; the smaller of the two wins.
/// </remarks>
public static class MigrationPackageLimits
{
    /// <summary>The most bytes the panel's header, or the agent's manifest, may be.</summary>
    public const int MaxHeaderBytes = 1024 * 1024;

    /// <summary>The most bytes a signature may be: a DER-encoded ECDSA P-384 signature.</summary>
    public const int MaxSignatureBytes = 104;

    /// <summary>The most bytes of artifact the panel agrees to relay in one package.</summary>
    public const long MaxArtifactBytes = 256L * 1024 * 1024 * 1024;

    /// <summary>How long an export stays available before its package is no longer served.</summary>
    public static readonly TimeSpan ExportRetention = TimeSpan.FromDays(7);

    /// <summary>How long a pull grant works when the administrator does not choose.</summary>
    public static readonly TimeSpan DefaultGrantLifetime = TimeSpan.FromMinutes(30);

    /// <summary>The longest a pull grant may be issued for.</summary>
    public static readonly TimeSpan MaxGrantLifetime = TimeSpan.FromMinutes(60);
}
