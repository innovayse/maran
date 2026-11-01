using Maran.Agent.Client.Services.AccountsService;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Interfaces;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates a plan's <see cref="RuntimeLimits"/> in the three shapes it travels in: the wire a screen
/// reads and sends, and the agent's slice ceilings.
/// </summary>
/// <remarks>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): every
/// case arrives already stated — <see cref="ResourceLimitKind"/> on one side, the agent's
/// <see cref="SliceCeilingKind"/> on the other — and is carried across, never inferred from a figure.
/// </remarks>
public static class RuntimeLimitsMapper
{
    /// <summary>Projects one ceiling onto the wire.</summary>
    /// <param name="limit">The plan's ceiling.</param>
    /// <returns>Its wire shape, case and figure together.</returns>
    public static ResourceCeilingDto ToDto(ResourceLimit limit)
    {
        return new ResourceCeilingDto(limit.Kind, limit.Maximum);
    }

    /// <summary>Reads the three ceilings a validated plan definition states.</summary>
    /// <param name="definition">A command that passed <see cref="Validators.PlanDefinitionValidator{TCommand}"/>.</param>
    /// <returns>The plan's runtime ceilings.</returns>
    public static RuntimeLimits ToDomain(IPlanDefinition definition)
    {
        return RuntimeLimits.Of(
            ToDomain(definition.CpuQuotaPercent),
            ToDomain(definition.MemoryMaxMb),
            ToDomain(definition.MaxProcesses));
    }

    /// <summary>Restates a plan's ceilings as the agent's slice ceilings.</summary>
    /// <param name="limits">The plan's runtime ceilings.</param>
    /// <returns>The ceilings the agent writes onto an account's slice.</returns>
    public static AccountSliceLimitsDto ToAgent(RuntimeLimits limits)
    {
        return new AccountSliceLimitsDto(
            ToAgent(limits.CpuQuotaPercent),
            ToAgent(limits.MemoryMaxMb),
            ToAgent(limits.MaxProcesses));
    }

    /// <summary>Reads one validated wire ceiling as the plan's.</summary>
    /// <param name="ceiling">The wire ceiling, already validated.</param>
    /// <returns>The plan's ceiling.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a kind the validator would have refused.</exception>
    private static ResourceLimit ToDomain(ResourceCeilingDto ceiling)
    {
        return ceiling.Kind switch
        {
            ResourceLimitKind.Limited => ResourceLimit.Limited(ceiling.Maximum),
            ResourceLimitKind.Unlimited => ResourceLimit.Unlimited(),
            _ => throw new ArgumentOutOfRangeException(nameof(ceiling), ceiling.Kind, "Unknown ceiling kind."),
        };
    }

    /// <summary>Restates one plan ceiling as the agent's.</summary>
    /// <param name="limit">The plan's ceiling; a limited one is positive by <see cref="RuntimeLimits"/>'s rule.</param>
    /// <returns>The agent's ceiling.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a kind this mapper does not know.</exception>
    private static SliceCeiling ToAgent(ResourceLimit limit)
    {
        return limit.Kind switch
        {
            ResourceLimitKind.Limited => SliceCeiling.Limited((uint)limit.Maximum),
            ResourceLimitKind.Unlimited => SliceCeiling.Unlimited(),
            _ => throw new ArgumentOutOfRangeException(nameof(limit), limit.Kind, "Unknown ceiling kind."),
        };
    }
}
