using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Services;
using AgentGrantInspectionReport = Maran.Agent.Client.Services.DbService.GrantInspectionReportDto;
using AgentGrantRefusal = Maran.Agent.Client.Services.DbService.AgentGrantRefusal;
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
/// Which pass produced the census is the TYPE the agent client hands over, not a flag carried in beside
/// it: an inspection becomes a preview and a repair becomes an applied report, so a repairing pass over a
/// host with nothing to repair is never reported as an inspection merely because both lists are empty.
/// </para>
/// </remarks>
public static class GrantRepairReportMapper
{
    /// <summary>Projects the census of a report-only pass onto the wire.</summary>
    /// <param name="report">What the agent read.</param>
    /// <param name="refusalText">Resolver for the two sentences each refusal carries.</param>
    /// <returns>The report as an administrator's screen reads it, its rows listed as a preview.</returns>
    public static GrantRepairReportDto ToDto(
        AgentGrantInspectionReport report,
        GrantRepairRefusalDisplayNames refusalText)
    {
        return new GrantRepairReportDto(
            report.ExaminedGrants,
            report.AlreadyCorrect,
            new GrantRepairPreviewDto(report.WouldRepair.Select(ToRepaired).ToList()),
            report.Refused.Select(grant => { return ToRefused(grant, refusalText); }).ToList());
    }

    /// <summary>Projects the census of a repairing pass onto the wire.</summary>
    /// <param name="report">What the agent read and did.</param>
    /// <param name="refusalText">Resolver for the two sentences each refusal carries.</param>
    /// <returns>The report as an administrator's screen reads it, its rows listed as applied.</returns>
    public static GrantRepairReportDto ToDto(
        AgentGrantRepairReport report,
        GrantRepairRefusalDisplayNames refusalText)
    {
        return new GrantRepairReportDto(
            report.ExaminedGrants,
            report.AlreadyCorrect,
            new GrantRepairAppliedDto(report.Repaired.Select(ToRepaired).ToList()),
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
        var reason = ReasonOf(grant.Reason);

        return new RefusedGrantDto(
            grant.GrantHost,
            grant.DatabaseName,
            grant.DbUsername,
            reason,
            refusalText.NameOf(reason),
            refusalText.AdviceOf(reason));
    }

    /// <summary>Restates the agent client's refusal reason as the module's own.</summary>
    /// <param name="reason">What the agent client reports.</param>
    /// <returns>The same reason in this module's vocabulary.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A reason this module has no name for.</exception>
    private static GrantRefusalReason ReasonOf(AgentGrantRefusal reason)
    {
        return reason switch
        {
            AgentGrantRefusal.HostIsNotLocalhost => GrantRefusalReason.HostIsNotLocalhost,
            AgentGrantRefusal.NotThePanelsNaming => GrantRefusalReason.NotThePanelsNaming,
            AgentGrantRefusal.UnrecognisedPrivileges => GrantRefusalReason.UnrecognisedPrivileges,
            AgentGrantRefusal.PartiallyOrUnfamiliarlyEscaped => GrantRefusalReason.PartiallyOrUnfamiliarlyEscaped,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown grant refusal reason."),
        };
    }
}
