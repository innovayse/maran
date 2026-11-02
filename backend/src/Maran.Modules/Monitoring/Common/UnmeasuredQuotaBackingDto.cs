namespace Maran.Modules.Monitoring.Common;

/// <summary>
/// The panel could not find out whether the filesystem enforces a quota: the agent's answer did
/// not arrive or could not be read. Neither "enforced" nor "not enforced" is claimed
/// (<c>state</c> is <c>unmeasured</c>).
/// </summary>
/// <param name="Note">The localized statement that enforcement could not be determined.</param>
public sealed record UnmeasuredQuotaBackingDto(string Note) : QuotaBackingDto;
