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
/// <c>isReportOnly</c> is carried in from the CALLER rather than inferred from the lists, for the same
/// reason <c>GrantRepairReportMapper</c> does: a repairing pass over a host with nothing to repair
/// produces exactly the same two empty lists a report-only pass over the same host does.
/// </para>
/// </remarks>
public static class HomeGroupRepairReportMapper
{
    /// <summary>Projects one census onto the wire.</summary>
    /// <param name="report">What the agent read and did.</param>
    /// <param name="isReportOnly">Whether the pass that produced <paramref name="report"/> changed nothing.</param>
    /// <param name="refusalText">Resolver for the two sentences each refusal carries.</param>
    /// <returns>The report as an administrator's screen reads it.</returns>
    public static HomeGroupRepairReportDto ToDto(
        AgentHomeGroupRepairReport report,
        bool isReportOnly,
        HomeGroupRepairRefusalDisplayNames refusalText)
    {
        return new HomeGroupRepairReportDto(
            isReportOnly,
            report.Examined,
            report.AlreadyCorrect,
            report.Repaired.Select(ToRepaired).ToList(),
            report.WouldRepair.Select(ToRepaired).ToList(),
            report.Refused.Select(home => { return ToRefused(home, refusalText); }).ToList());
    }

    /// <summary>Projects one re-grouped — or would-be-re-grouped — home.</summary>
    /// <param name="home">The agent's row.</param>
    /// <returns>The wire row.</returns>
    private static RepairedHomeDto ToRepaired(AgentRepairedHome home)
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
