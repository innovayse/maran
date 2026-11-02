using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Services;
using AgentHomeGroupRepairReport = Maran.Agent.Client.Services.AccountsService.HomeGroupRepairReportDto;
using AgentRefusedHome = Maran.Agent.Client.Services.AccountsService.RefusedHomeDto;
using AgentRepairedHome = Maran.Agent.Client.Services.AccountsService.RepairedHomeDto;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates the agent's home-group repair census as the wire shape an administrator's screen reads.
/// </summary>
/// <remarks>
/// <para>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): the four
/// buckets arrive already sorted by the agent, and every decision about which bucket an account
/// belongs in was made by the predicate that runs on the host. What this adds is the two localized
/// sentences a refusal needs, read from <see cref="HomeGroupRepairRefusalDisplayNames"/> — a lookup,
/// not a judgement.
/// </para>
/// <para>
/// The CASE is stated by which method the CALLER chose, never inferred from the lists: a repairing
/// pass over a host with nothing to repair produces exactly the same two empty lists a report-only
/// pass over the same host does. The agent's census always carries both lists and leaves one of them
/// empty; each method reads the one its pass fills and drops the other.
/// </para>
/// </remarks>
public static class HomeGroupRepairReportMapper
{
    /// <summary>Projects the census of a report-only pass onto the wire.</summary>
    /// <param name="report">What the agent read; nothing was changed.</param>
    /// <param name="refusalText">Resolver for the two sentences each refusal carries.</param>
    /// <returns>The report, whose work lists the homes a repair would re-group.</returns>
    public static HomeGroupRepairReportDto ToPreview(
        AgentHomeGroupRepairReport report,
        HomeGroupRepairRefusalDisplayNames refusalText)
    {
        return new HomeGroupRepairReportDto(
            report.Examined,
            report.AlreadyCorrect,
            new HomeGroupRepairPreviewDto(report.WouldRepair.Select(ToRepairedHome).ToList()),
            report.Refused.Select(home => { return ToRefused(home, refusalText); }).ToList());
    }

    /// <summary>Projects the census of a repairing pass onto the wire.</summary>
    /// <param name="report">What the agent read and did.</param>
    /// <param name="refusalText">Resolver for the two sentences each refusal carries.</param>
    /// <returns>The report, whose work lists the homes that were re-grouped.</returns>
    public static HomeGroupRepairReportDto ToApplied(
        AgentHomeGroupRepairReport report,
        HomeGroupRepairRefusalDisplayNames refusalText)
    {
        return new HomeGroupRepairReportDto(
            report.Examined,
            report.AlreadyCorrect,
            new HomeGroupRepairAppliedDto(report.Repaired.Select(ToRepairedHome).ToList()),
            report.Refused.Select(home => { return ToRefused(home, refusalText); }).ToList());
    }

    /// <summary>Projects one re-grouped — or would-be-re-grouped — home.</summary>
    /// <param name="home">The agent's row.</param>
    /// <returns>The wire row.</returns>
    private static RepairedHomeDto ToRepairedHome(AgentRepairedHome home)
    {
        return new RepairedHomeDto(home.AccountUsername, home.Home);
    }

    /// <summary>Projects one refused account and names its reason.</summary>
    /// <param name="home">The agent's row, carrying the passwd database's raw fields.</param>
    /// <param name="refusalText">Resolver for the two sentences.</param>
    /// <returns>
    /// The wire row. The account name and path are copied VERBATIM — a refused row is refused because
    /// it is not a home this panel's own account creation produced, and tidying it here would hide the
    /// thing an operator has to read.
    /// </returns>
    private static RefusedHomeDto ToRefused(AgentRefusedHome home, HomeGroupRepairRefusalDisplayNames refusalText)
    {
        return new RefusedHomeDto(
            home.AccountUsername,
            home.Home,
            home.Reason,
            refusalText.NameOf(home.Reason),
            refusalText.AdviceOf(home.Reason));
    }
}
