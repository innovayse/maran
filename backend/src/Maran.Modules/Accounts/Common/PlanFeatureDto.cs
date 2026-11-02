using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Common;

/// <summary>One feature a plan can sell, with the name an administrator reads it by.</summary>
/// <param name="Feature">
/// The feature's name on the wire (<c>mail</c>, <c>fileTransfer</c>, ...): what a plan's
/// <c>features</c> list and the customer's own <c>features</c> list hold, and what the form sends
/// back.
/// </param>
/// <param name="DisplayName">The feature's name resolved server-side for the request's culture.</param>
public sealed record PlanFeatureDto(AccountFeature Feature, string DisplayName);
