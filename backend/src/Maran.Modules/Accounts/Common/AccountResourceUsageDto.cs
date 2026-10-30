using Maran.Modules.Accounts.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// One row of an account's resource summary: how many of one kind the account owns, beside what its
/// plan allows. Both halves state their own case, so no number stands in for a state.
/// </summary>
/// <param name="Kind">The kind of resource.</param>
/// <param name="State">
/// Whether the owning module could count; <see cref="AccountResourceCountState.Unavailable"/> when the
/// agent holding the truth did not answer.
/// </param>
/// <param name="Used">The count when <paramref name="State"/> is <c>Counted</c>; zero otherwise.</param>
/// <param name="LimitKind">Whether the plan caps this kind at all.</param>
/// <param name="Limit">The plan's cap when <paramref name="LimitKind"/> is <c>Limited</c>; zero otherwise.</param>
public sealed record AccountResourceUsageDto(
    AccountResourceKind Kind,
    AccountResourceCountState State,
    int Used,
    ResourceLimitKind LimitKind,
    int Limit);
