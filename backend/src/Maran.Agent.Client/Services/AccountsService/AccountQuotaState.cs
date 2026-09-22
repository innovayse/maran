namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// What the agent can honestly say about one account's disk quota — never a bare byte count,
/// because a byte count alone cannot tell "no limit configured" apart from "this filesystem
/// cannot enforce anything at all". Mirrors the agent's own <c>ops::accounts::QuotaState</c>.
/// </summary>
/// <remarks>
/// This is the fix for issue #29's own filed defect: before this field existed, both
/// "enforceable, no limit set" and "this filesystem cannot enforce anything" collapsed onto the
/// same zero-valued <see cref="AccountUsageDto.QuotaBytes"/>, and an operator reading a zero could
/// not tell a deliberate business decision from a filesystem that was never mounted with quota
/// accounting.
/// </remarks>
public enum AccountQuotaState
{
    /// <summary>The agent predates this field. A caller MUST fall back to <see cref="AccountUsageDto.QuotaBytes"/>.</summary>
    Unspecified = 0,

    /// <summary>The filesystem can enforce a quota, and this account has one — read <see cref="AccountUsageDto.QuotaBytes"/> for the figure.</summary>
    Enforced = 1,

    /// <summary>The filesystem can enforce a quota, and no limit is configured for this account.</summary>
    EnforceableButUnset = 2,

    /// <summary>
    /// The filesystem cannot enforce anything right now. <see cref="AccountUsageDto.QuotaBytes"/>
    /// is zero here and means nothing — see <see cref="AccountUsageDto.QuotaUnenforceableReason"/>
    /// for why.
    /// </summary>
    NotEnforceable = 3,
}
