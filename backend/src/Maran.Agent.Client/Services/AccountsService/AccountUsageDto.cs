namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>An account's disk usage as the agent measured it.</summary>
/// <param name="UsedBytes">Bytes currently occupied by the account's home directory tree.</param>
/// <param name="QuotaBytes">
/// DEPRECATED mirror of <paramref name="QuotaState"/>'s byte figure: equal to the enforced quota
/// when <paramref name="QuotaState"/> is <see cref="AccountQuotaState.Enforced"/>, and zero for
/// both <see cref="AccountQuotaState.EnforceableButUnset"/> and
/// <see cref="AccountQuotaState.NotEnforceable"/>. A zero here NEVER distinguishes "no limit
/// configured" from "this filesystem cannot hold a limit at all" — that is exactly the collapse
/// <paramref name="QuotaState"/> exists to end. New callers read <paramref name="QuotaState"/>.
/// </param>
/// <param name="QuotaState">
/// What the agent can honestly say about this account's disk quota. See
/// <see cref="AccountQuotaState"/>.
/// </param>
/// <param name="QuotaUnenforceableReason">
/// Present only when <paramref name="QuotaState"/> is <see cref="AccountQuotaState.NotEnforceable"/>.
/// </param>
public sealed record AccountUsageDto(
    ulong UsedBytes,
    ulong QuotaBytes,
    AccountQuotaState QuotaState,
    QuotaUnenforceableReason QuotaUnenforceableReason);
