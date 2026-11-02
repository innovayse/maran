namespace Maran.Modules.Monitoring.Common;

/// <summary>
/// The agent reported that the filesystem cannot enforce a quota right now. The allowance is
/// real — it is still the plan the customer paid for — but nothing on the server refuses a write
/// past it (<c>state</c> is <c>notEnforced</c>).
/// </summary>
/// <param name="Note">The localized explanation of what is wrong and what restores enforcement.</param>
public sealed record NotEnforcedQuotaBackingDto(string Note) : QuotaBackingDto;
