using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates a <see cref="Plan"/> as the wire shape a screen reads, and is the one place that decides
/// which of a plan's two kinds of name is the one to show.
/// </summary>
/// <remarks>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): the
/// choice between the two names is not a judgement about the plan but a fact about it — a plan an
/// operator named carries that text, and a plan the panel ships carries a resource key — so reading
/// it off the plan is a restatement. It exists so that the three readers of a plan's name (the plan
/// list, the customer's own account, and the answer to every plan mutation) cannot come to disagree
/// about it, which is how a customer would end up reading a different name for their plan than the
/// administrator who sold it.
/// </remarks>
public static class PlanMapper
{
    /// <summary>Names a plan in the request's culture.</summary>
    /// <param name="plan">The plan to name.</param>
    /// <param name="displayNames">Resolves a shipped plan's resource key in the request's culture.</param>
    /// <returns>
    /// The operator's own text, verbatim, for a plan an operator named; the translated name for a
    /// plan the panel ships.
    /// </returns>
    public static string DisplayNameOf(Plan plan, IStringLocalizer<DisplayNames> displayNames)
    {
        return plan.Name ?? displayNames[plan.DisplayNameKey].Value;
    }

    /// <summary>Projects one plan onto the wire.</summary>
    /// <param name="plan">The plan to project.</param>
    /// <param name="displayNames">Resolves a shipped plan's resource key in the request's culture.</param>
    /// <param name="accountCount">How many accounts are on the plan right now.</param>
    /// <returns>The plan's outward view.</returns>
    public static PlanDto ToDto(Plan plan, IStringLocalizer<DisplayNames> displayNames, int accountCount)
    {
        return new PlanDto(
            plan.Id,
            DisplayNameOf(plan, displayNames),
            plan.DiskQuotaMb,
            plan.MaxSites,
            plan.MaxDatabases,
            plan.MaxSftpUsers,
            plan.MaxFtpUsers,
            plan.MaxCronEntries,
            plan.MaxPhpWorkersPerPool,
            accountCount);
    }
}
