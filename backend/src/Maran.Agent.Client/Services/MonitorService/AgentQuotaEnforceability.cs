using Maran.Agent.Client.Services.AccountsService;

namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>
/// What the agent found when it checked whether the filesystem holding hosting accounts' homes
/// can currently enforce a per-user disk quota: exactly one of two cases, each its own type, so a
/// reason exists only on the case that has one.
/// </summary>
/// <remarks>
/// <para>
/// A remount can change this without touching any account — see the Monitoring module's continuous
/// check, which re-asks this on a schedule rather than trusting the installer's one-time preflight
/// warning. A network filesystem mounted for <c>/home</c> — NFS, most concretely — can report a
/// LOCAL state disconnected from server-side enforcement; the agent has no rpc path to a remote
/// NFS server's quota daemon, and this is a known, named gap.
/// </para>
/// <para>
/// <b>There is no third case for "the agent did not say".</b> A reply whose enforceability the
/// panel cannot name — the wire's unspecified value, or one from an agent newer than this panel —
/// is not an <see cref="AgentQuotaEnforceability"/> at all: the client answers it with
/// <c>AgentInvalidResponse</c>, so a caller sees a failed read (which it already treats as "no
/// evidence either way") and never a fabricated finding. The constructor is private to this
/// assembly, so the two nested cases are the whole set and a switch over them is exhaustive by
/// construction.
/// </para>
/// </remarks>
public abstract record AgentQuotaEnforceability
{
    /// <summary>Closes the hierarchy to the two cases declared below.</summary>
    private AgentQuotaEnforceability()
    {
    }

    /// <summary>
    /// The filesystem is tracking quota accounting right now (<c>quotaon -p</c> says so), so a limit
    /// set on an account is backed by the kernel.
    /// </summary>
    public sealed record Enforceable : AgentQuotaEnforceability;

    /// <summary>The filesystem cannot currently enforce a per-user quota, for a stated reason.</summary>
    /// <param name="Reason">Why the agent says the filesystem cannot hold an enforceable quota.</param>
    public sealed record NotEnforceable(QuotaUnenforceableReason Reason) : AgentQuotaEnforceability;
}
