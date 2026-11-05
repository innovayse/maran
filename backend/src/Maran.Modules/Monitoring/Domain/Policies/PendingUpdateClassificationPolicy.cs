using Maran.Agent.Client.Services.MonitorService;

namespace Maran.Modules.Monitoring.Domain.Policies;

/// <summary>
/// The panel's three words for what the agent measured about one pending update, and the one place a
/// measurement becomes a word or a sort rank (issue #163).
/// </summary>
/// <remarks>
/// <para>
/// <b>Unmeasured is a third answer, not a flavour of ordinary.</b> An agent that predates the
/// classification, or a host whose security reading failed, says nothing about the update, and
/// calling that "ordinary" would tell an administrator a package is not a security fix when nobody
/// checked. The three words are the whole vocabulary of the <c>classification</c> filter and of the
/// <c>classification</c> member of a row, so the sets cannot drift apart.
/// </para>
/// </remarks>
public static class PendingUpdateClassificationPolicy
{
    /// <summary>The update is marked as a security fix by the host's package manager.</summary>
    public const string Security = "security";

    /// <summary>The update was measured and is not marked as a security fix.</summary>
    public const string Ordinary = "ordinary";

    /// <summary>The agent did not measure whether the update is a security fix.</summary>
    public const string Unmeasured = "unmeasured";

    /// <summary>Every word a row can carry and every value the filter accepts, in sort-rank order.</summary>
    public static readonly IReadOnlyList<string> All = [Security, Ordinary, Unmeasured];

    /// <summary>Returns the word for what the agent measured.</summary>
    /// <param name="classification">The agent's reading, already free of the wire's zero.</param>
    /// <returns>One of <see cref="All"/>.</returns>
    public static string NameOf(AgentUpdateClassification classification)
    {
        return classification switch
        {
            AgentUpdateClassification.Security => Security,
            AgentUpdateClassification.Ordinary => Ordinary,
            _ => Unmeasured,
        };
    }

    /// <summary>Returns the display-name resource key that names one of the three words.</summary>
    /// <param name="classification">One of <see cref="All"/>.</param>
    /// <returns>The key in <c>DisplayNames.resx</c>; one entry per word, in every language.</returns>
    public static string DisplayNameKeyOf(string classification)
    {
        return classification switch
        {
            Security => "PendingUpdateClassificationSecurity",
            Ordinary => "PendingUpdateClassificationOrdinary",
            _ => "PendingUpdateClassificationUnmeasured",
        };
    }

    /// <summary>
    /// Returns where a reading falls when the list is sorted by classification: security first, then
    /// ordinary, then what was not measured, so ascending puts the updates that matter on top.
    /// </summary>
    /// <param name="classification">The agent's reading.</param>
    /// <returns>The rank, lowest first.</returns>
    public static int RankOf(AgentUpdateClassification classification)
    {
        return classification switch
        {
            AgentUpdateClassification.Security => 0,
            AgentUpdateClassification.Ordinary => 1,
            _ => 2,
        };
    }
}
