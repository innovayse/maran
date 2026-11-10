namespace Maran.Modules.ServerMigration.Domain.Policies;

/// <summary>
/// The fixed time bounds of pulling a package from another panel (issue #127), in the same spirit as
/// <see cref="MigrationPackageLimits"/>: constants, because a bound an operator can raise is a bound a thief of the
/// configuration can remove.
/// </summary>
public static class MigrationPullLimits
{
    /// <summary>How long a new connection to the other panel may take to be made.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long the other panel may take to answer with its response headers.</summary>
    public static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The longest a single read of the body may wait for the next bytes. The body as a whole is bounded by the
    /// signed length, not by time, because a package of hundreds of gigabytes legitimately takes hours; a stalled
    /// peer is what this ends.
    /// </summary>
    public static readonly TimeSpan ReadStall = TimeSpan.FromSeconds(120);

    /// <summary>How much longer than <see cref="HeaderTimeout"/> the Host's backstop waits, so the client's own deadline fires first.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);
}
