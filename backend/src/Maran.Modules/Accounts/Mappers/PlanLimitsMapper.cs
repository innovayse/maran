using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Interfaces;
using Maran.Sdk.Extensions;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Reads the figures a validated plan definition states as the <see cref="PlanLimits"/> the reseller
/// envelope is judged against, so a plan is judged BEFORE it is created or edited.
/// </summary>
/// <remarks>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): every
/// figure is copied from the definition, and whether any of them is acceptable is
/// <see cref="Domain.Policies.ResellerEnvelopePolicy"/>'s question.
/// </remarks>
public static class PlanLimitsMapper
{
    /// <summary>Restates a plan definition's figures.</summary>
    /// <param name="definition">A command that passed <see cref="Validators.PlanDefinitionValidator{TCommand}"/>.</param>
    /// <param name="mailLimits">
    /// The mail figures the plan would hold after the write: the definition's own when it states them,
    /// otherwise the default on creation or the plan's current ones on an edit. The handler resolves
    /// that, because "not stated" means a different thing in each, and this mapper only restates.
    /// </param>
    /// <returns>The figures of the plan the definition would sell.</returns>
    public static PlanLimits ToLimits(IPlanDefinition definition, MailLimits mailLimits)
    {
        return new PlanLimits(
            definition.DiskQuotaMb,
            definition.MaxSites,
            definition.MaxDatabases,
            definition.MaxSftpUsers,
            definition.MaxFtpUsers,
            definition.MaxCronEntries,
            definition.MaxPhpWorkersPerPool,
            definition.Features.ToSet(),
            RuntimeLimitsMapper.ToDomain(definition),
            mailLimits.MaxMailboxes,
            mailLimits.MailboxQuotaPoolMib);
    }
}
