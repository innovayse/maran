using Maran.Agent.Client.Services.AccountsService;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Interfaces;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates a plan's <see cref="RuntimeLimits"/> in the three shapes it travels in: the wire a screen
/// reads and sends, and the agent's slice ceilings.
/// </summary>
/// <remarks>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): every
/// case arrives already stated — a cap or its absence on the stored side, the wire's
/// <see cref="LimitedResourceCeilingDto"/> or <see cref="UnlimitedResourceCeilingDto"/>, the agent's
/// <see cref="SliceCeilingKind"/> on the other — and is carried across, never inferred from a figure.
/// </remarks>
public static class RuntimeLimitsMapper
{
    /// <summary>Projects one ceiling onto the wire.</summary>
    /// <param name="limit">The plan's ceiling.</param>
    /// <returns>Its wire shape: a figure travels only with a capped ceiling.</returns>
    public static ResourceCeilingDto ToDto(ResourceLimit limit)
    {
        return limit.Maximum is int maximum
            ? new LimitedResourceCeilingDto(maximum)
            : new UnlimitedResourceCeilingDto();
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
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a shape the validator would have refused.</exception>
    private static ResourceLimit ToDomain(ResourceCeilingDto ceiling)
    {
        return ceiling switch
        {
            LimitedResourceCeilingDto limited => ResourceLimit.Limited(limited.Maximum),
            UnlimitedResourceCeilingDto => ResourceLimit.Unlimited(),
            _ => throw new ArgumentOutOfRangeException(nameof(ceiling), ceiling, "Unknown ceiling shape."),
        };
    }

    /// <summary>Restates one plan ceiling as the agent's.</summary>
    /// <param name="limit">The plan's ceiling; a limited one is positive by <see cref="RuntimeLimits"/>'s rule.</param>
    /// <returns>The agent's ceiling.</returns>
    private static SliceCeiling ToAgent(ResourceLimit limit)
    {
        return limit.Maximum is int maximum
            ? SliceCeiling.Limited((uint)maximum)
            : SliceCeiling.Unlimited();
    }
}
