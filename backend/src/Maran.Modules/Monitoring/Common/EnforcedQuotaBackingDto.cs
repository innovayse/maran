namespace Maran.Modules.Monitoring.Common;

/// <summary>
/// The filesystem holding this account's home is tracking quota accounting right now, so the
/// allowance is backed by a kernel-enforced limit and there is nothing to explain
/// (<c>state</c> is <c>enforced</c>).
/// </summary>
public sealed record EnforcedQuotaBackingDto : QuotaBackingDto;
