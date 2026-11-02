namespace Maran.Modules.Accounts.Queries.ListPlanFeatures;

/// <summary>
/// Lists every feature a plan can sell, so the plan form offers exactly the set the panel
/// understands and the SPA holds no list of its own (rules/architecture.md "The backend owns the
/// data, the SPA renders it"). Takes no parameters: the set is short and fixed.
/// </summary>
public sealed record ListPlanFeaturesQuery;
