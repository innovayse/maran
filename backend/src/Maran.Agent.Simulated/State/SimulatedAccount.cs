using Maran.Agent.Client.Services.AccountsService;

namespace Maran.Agent.Simulated.State;

/// <summary>One hosting account on the simulated host: the operating-system identity behind it.</summary>
/// <param name="Username">The system user name; the account's key.</param>
/// <param name="Uid">The numeric user id the host gave it.</param>
/// <param name="HomeDirectory">Where its files live.</param>
/// <param name="QuotaBytes">Its disk quota in bytes; zero is no limit.</param>
/// <param name="CreatedAt">When it was created, the origin its usage grows from.</param>
/// <param name="Suspended">Whether its own login is locked.</param>
/// <param name="Limits">The resource ceilings of its systemd slice.</param>
/// <param name="PinnedUsedBytes">
/// When set, the disk usage reported verbatim; when null, usage follows the deterministic curve in
/// <see cref="Usage.SimulatedUsageCurve"/>. A seed pins a figure to put an account over its quota.
/// </param>
/// <param name="InodeLimit">The ceiling on files and directories, or null for none.</param>
/// <param name="Shell">The login shell; suspension takes it away and reactivation gives one back.</param>
/// <param name="OpenFilesLimit">The ceiling on open files for the account's sessions, or null for none.</param>
public sealed record SimulatedAccount(
    string Username,
    uint Uid,
    string HomeDirectory,
    ulong QuotaBytes,
    DateTimeOffset CreatedAt,
    bool Suspended,
    AccountSliceLimitsDto Limits,
    ulong? PinnedUsedBytes,
    ulong? InodeLimit = null,
    AccountLoginShell Shell = AccountLoginShell.Nologin,
    ulong? OpenFilesLimit = null)
{
    /// <summary>The slice ceilings a new account starts with: none.</summary>
    public static readonly AccountSliceLimitsDto UnlimitedSlices = new(
        SliceCeiling.Unlimited(),
        SliceCeiling.Unlimited(),
        SliceCeiling.Unlimited());
}
