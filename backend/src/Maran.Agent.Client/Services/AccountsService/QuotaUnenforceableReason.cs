namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// The reason a filesystem cannot hold an enforceable disk quota, as far as the agent can tell
/// from where it is standing. Present only when <see cref="AccountUsageDto.QuotaState"/> is
/// <see cref="AccountQuotaState.NotEnforceable"/>. Mirrors the agent's own
/// <c>ops::accounts::QuotaUnenforceableReason</c>.
/// </summary>
/// <remarks>
/// A network filesystem mounted for <c>/home</c> — NFS, most concretely — can report a LOCAL
/// state disconnected from server-side enforcement (<c>rpc.rquotad</c>); the agent has no rpc path
/// to a remote NFS server's quota daemon, and this is a known gap, not one either side pretends to
/// close.
/// </remarks>
public enum QuotaUnenforceableReason
{
    /// <summary>The agent predates this field, or the state is not <see cref="AccountQuotaState.NotEnforceable"/>.</summary>
    Unspecified = 0,

    /// <summary>
    /// The filesystem is not mounted with a quota-accounting option
    /// (<c>usrquota</c>/<c>uquota</c>/<c>usrjquota</c>).
    /// </summary>
    MountedWithoutQuotaAccounting = 1,

    /// <summary>Mounted with the option, but <c>quotaon -p</c> reports accounting is off.</summary>
    AccountingNotEnabled = 2,
}
