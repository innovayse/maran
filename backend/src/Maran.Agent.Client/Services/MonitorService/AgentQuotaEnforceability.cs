using Maran.Agent.Client.Services.AccountsService;

namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>
/// What the agent found when it checked whether the filesystem holding hosting accounts' homes
/// can currently enforce a per-user disk quota.
/// </summary>
/// <param name="IsEnforceable">
/// <c>true</c> when <c>quotaon -p</c> reports the filesystem is tracking quota accounting right
/// now; <c>false</c> for the stated <paramref name="Reason"/>.
/// </param>
/// <param name="Reason">
/// Present only when <paramref name="IsEnforceable"/> is <c>false</c>.
/// </param>
/// <remarks>
/// A remount can change this without touching any account — see the Monitoring module's continuous
/// check, which re-asks this on a schedule rather than trusting the installer's one-time preflight
/// warning. A network filesystem mounted for <c>/home</c> — NFS, most concretely — can report a
/// LOCAL state disconnected from server-side enforcement; the agent has no rpc path to a remote
/// NFS server's quota daemon, and this is a known, named gap.
/// </remarks>
public sealed record AgentQuotaEnforceability(bool IsEnforceable, QuotaUnenforceableReason Reason);
