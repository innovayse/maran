using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Services;
using AgentGrantRepairReport = Maran.Agent.Client.Services.DbService.GrantRepairReportDto;
using AgentRefusedGrant = Maran.Agent.Client.Services.DbService.RefusedGrantDto;
using AgentRepairedGrant = Maran.Agent.Client.Services.DbService.RepairedGrantDto;

namespace Maran.Modules.Databases.Mappers;

/// <summary>
/// Restates the agent's grant-table census as the wire shape an administrator's screen reads.
/// </summary>
/// <remarks>
/// <para>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): the four
/// buckets arrive already sorted by the agent, and every decision about which bucket a row belongs in
/// was made on the server that holds the rows. What this adds is the two localized sentences a refusal
/// needs, read from <see cref="GrantRepairRefusalDisplayNames"/> — a lookup, not a judgement.
/// </para>
/// <para>
/// <c>isReportOnly</c> is carried in from the CALLER rather than inferred from the lists, and the
/// difference matters: a repairing pass over a host with nothing to repair produces exactly the same
/// two empty lists a report-only pass over the same host does, so inferring it would report an action
/// as an inspection precisely when both are empty.
/// </para>
/// </remarks>
public static class GrantRepairReportMapper
{
    /// <summary>Projects one census onto the wire.</summary>
    /// <param name="report">What the agent read and did.</param>
    /// <param name="isReportOnly">Whether the pass that produced <paramref name="report"/> changed nothing.</param>
    /// <param name="refusalText">Resolver for the two sentences each refusal carries.</param>
    /// <returns>The report as an administrator's screen reads it.</returns>
    public static GrantRepairReportDto ToDto(
        AgentGrantRepairReport report,
        bool isReportOnly,
        GrantRepairRefusalDisplayNames refusalText)
    {
        return new GrantRepairReportDto(
            isReportOnly,
            report.ExaminedGrants,
            report.AlreadyCorrect,
            report.Repaired.Select(ToRepaired).ToList(),
            report.WouldRepair.Select(ToRepaired).ToList(),
            report.Refused.Select(grant => { return ToRefused(grant, refusalText); }).ToList());
    }

    /// <summary>Projects one repaired — or would-be-repaired — row.</summary>
    /// <param name="grant">The agent's row.</param>
    /// <returns>The wire row, exposure list carried through unchanged.</returns>
    private static RepairedGrantDto ToRepaired(AgentRepairedGrant grant)
    {
        return new RepairedGrantDto(grant.DatabaseName, grant.DbUsername, grant.AlsoMatchedDatabases);
    }

    /// <summary>Projects one refused row and names its reason.</summary>
    /// <param name="grant">The agent's row, carrying the server's raw columns.</param>
    /// <param name="refusalText">Resolver for the two sentences.</param>
    /// <returns>
    /// The wire row. The three names are copied VERBATIM — a refused row is refused because it is not
    /// a value this panel could have written, and tidying it here would hide the thing an operator has
    /// to read.
    /// </returns>
    private static RefusedGrantDto ToRefused(AgentRefusedGrant grant, GrantRepairRefusalDisplayNames refusalText)
    {
        return new RefusedGrantDto(
            grant.GrantHost,
            grant.DatabaseName,
            grant.DbUsername,
            grant.Reason,
            refusalText.NameOf(grant.Reason),
            refusalText.AdviceOf(grant.Reason));
    }
}
