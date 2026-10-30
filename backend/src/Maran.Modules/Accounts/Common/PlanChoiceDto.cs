namespace Maran.Modules.Accounts.Common;

/// <summary>
/// One entry of the plan picker: the plan's identity to send back, the name to choose it by, and the
/// two limits the picker prints beside the name so an operator sees what the choice sells.
/// </summary>
/// <param name="Id">The plan's identity.</param>
/// <param name="DisplayName">The plan's name, resolved server-side for the request's culture.</param>
/// <param name="DiskQuotaMb">The disk quota the plan sells, in megabytes.</param>
/// <param name="MaxSites">How many sites an account on the plan may create.</param>
public sealed record PlanChoiceDto(Guid Id, string DisplayName, int DiskQuotaMb, int MaxSites);
