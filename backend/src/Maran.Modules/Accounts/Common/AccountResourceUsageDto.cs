using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// One row of an account's resource summary: how many of one kind the account owns, beside what its
/// plan allows. Each half is a union that states its own case, so no number stands in for a state.
/// </summary>
/// <param name="Kind">The kind of resource.</param>
/// <param name="Count">
/// The count when the owning module could take it, or the statement that the agent holding the truth
/// did not answer.
/// </param>
/// <param name="Limit">The plan's cap for this kind, or the statement that the plan states none.</param>
public sealed record AccountResourceUsageDto(
    AccountResourceKind Kind,
    ResourceCountDto Count,
    ResourceCeilingDto Limit);
