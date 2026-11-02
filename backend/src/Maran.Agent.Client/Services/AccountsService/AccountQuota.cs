namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// What the agent can honestly say about one account's disk quota — never a bare byte count,
/// because a byte count alone cannot tell "no limit configured" apart from "this filesystem
/// cannot enforce anything at all". Exactly one of three cases, each its own type, so a byte figure
/// exists only on the case that has one and a reason only on the case that has one. Mirrors the
/// agent's own <c>ops::accounts::QuotaState</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the fix for issue #29's own filed defect: before the state existed, both "enforceable,
/// no limit set" and "this filesystem cannot enforce anything" collapsed onto one zero-valued byte
/// figure, and an operator reading a zero could not tell a deliberate business decision from a
/// filesystem that was never mounted with quota accounting.
/// </para>
/// <para>
/// <b>There is no case for "the agent did not say".</b> The wire's unspecified state means the agent
/// predates the field; the client answers it with <c>AgentInvalidResponse</c> rather than inventing a
/// case, and the deprecated wire byte figure that older callers fell back to is no longer read at
/// all. The constructor is private to this assembly, so the three nested cases are the whole set.
/// </para>
/// </remarks>
public abstract record AccountQuota
{
    /// <summary>Closes the hierarchy to the three cases declared below.</summary>
    private AccountQuota()
    {
    }

    /// <summary>The filesystem can enforce a quota, and this account has one.</summary>
    /// <param name="Bytes">The enforced limit, in bytes.</param>
    public sealed record Enforced(ulong Bytes) : AccountQuota;

    /// <summary>The filesystem can enforce a quota, and no limit is configured for this account.</summary>
    public sealed record EnforceableButUnset : AccountQuota;

    /// <summary>The filesystem cannot enforce anything right now.</summary>
    /// <param name="Reason">Why the agent says it cannot.</param>
    public sealed record NotEnforceable(QuotaUnenforceableReason Reason) : AccountQuota;
}
