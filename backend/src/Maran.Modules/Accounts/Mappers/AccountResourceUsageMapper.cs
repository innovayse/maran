using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates one module's count and the plan's limit for the same kind as a row of the resource
/// summary, with each half in the wire shape that carries a number only where one was measured.
/// </summary>
/// <remarks>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): the
/// count's state and the limit's kind arrive already stated and are carried across, never inferred
/// from a figure.
/// </remarks>
public static class AccountResourceUsageMapper
{
    /// <summary>Projects one count and its plan limit onto a summary row.</summary>
    /// <param name="count">The owning module's answer for the kind.</param>
    /// <param name="limit">The plan's limit for the same kind.</param>
    /// <returns>The row.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a count state this mapper does not know.</exception>
    public static AccountResourceUsageDto ToDto(AccountResourceCount count, ResourceLimit limit)
    {
        ResourceCountDto shown = count.State switch
        {
            AccountResourceCountState.Counted => new CountedResourceCountDto(count.Used),
            AccountResourceCountState.Unavailable => new UnavailableResourceCountDto(),
            _ => throw new ArgumentOutOfRangeException(nameof(count), count.State, "Unknown count state."),
        };

        return new AccountResourceUsageDto(count.Kind, shown, RuntimeLimitsMapper.ToDto(limit));
    }
}
