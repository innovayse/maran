namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// The reason a filesystem cannot hold an enforceable disk quota, as far as the agent can tell
/// from where it is standing. It exists only inside a "not enforceable" answer
/// (<see cref="AccountQuota.NotEnforceable"/>, <c>AgentQuotaEnforceability.NotEnforceable</c>), so
/// it has no member for "no reason": an enforceable filesystem has no reason, and a reply that does
/// not name one is refused by the client instead of being given a member here. Mirrors the agent's
/// own <c>ops::accounts::QuotaUnenforceableReason</c>.
/// </summary>
/// <remarks>
/// A network filesystem mounted for <c>/home</c> — NFS, most concretely — can report a LOCAL
/// state disconnected from server-side enforcement (<c>rpc.rquotad</c>); the agent has no rpc path
/// to a remote NFS server's quota daemon, and this is a known gap, not one either side pretends to
/// close.
/// </remarks>
public enum QuotaUnenforceableReason
{
    /// <summary>
    /// The filesystem is not mounted with a quota-accounting option
    /// (<c>usrquota</c>/<c>uquota</c>/<c>usrjquota</c>).
    /// </summary>
    MountedWithoutQuotaAccounting,

    /// <summary>Mounted with the option, but <c>quotaon -p</c> reports accounting is off.</summary>
    AccountingNotEnabled,
}
