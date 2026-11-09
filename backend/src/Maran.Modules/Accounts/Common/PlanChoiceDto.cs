namespace Maran.Modules.Accounts.Common;

/// <summary>
/// One entry of the plan picker: the plan's identity to send back, the name to choose it by, and the
/// two limits the picker prints beside the name so an operator sees what the choice sells, and the
/// process ceiling the new-account form starts its own process field from (issue #194).
/// </summary>
/// <param name="Id">The plan's identity.</param>
/// <param name="DisplayName">The plan's name, resolved server-side for the request's culture.</param>
/// <param name="DiskQuotaMb">The disk quota the plan sells, in megabytes.</param>
/// <param name="MaxSites">How many sites an account on the plan may create.</param>
/// <param name="MaxProcesses">
/// The most processes and threads an account on the plan may run at once, or no ceiling, in the shape
/// every plan ceiling travels in. A reseller may only state a figure at or below it for an account.
/// </param>
public sealed record PlanChoiceDto(Guid Id, string DisplayName, int DiskQuotaMb, int MaxSites, ResourceCeilingDto MaxProcesses);
