using Maran.Agent.Client.Services.CronService;

namespace Maran.Agent.Simulated.State;

/// <summary>
/// One account's crontab as the simulated server holds it. Mutable on purpose and reachable only
/// through <see cref="SimulatedCronStore"/>, which holds the lock every read and write of it takes.
/// </summary>
internal sealed class SimulatedCronTable
{
    /// <summary>The managed entries in the order they were installed.</summary>
    public List<AgentCronEntry> Entries { get; } = [];

    /// <summary>The managed environment assignments in the order the crontab holds them.</summary>
    public List<AgentCronEnvVar> Environment { get; } = [];

    /// <summary>
    /// What each entry's most recent run left behind, keyed by entry id; absent for an entry that never ran.
    /// </summary>
    public Dictionary<string, AgentCronRunOutput> LastRuns { get; } = [];

    /// <summary>
    /// How many entry ids this crontab has minted, so two entries never share one even after a deletion.
    /// </summary>
    public int MintedIds { get; set; }

    /// <summary>Whether every managed entry is suppressed; the entries keep their own enablement.</summary>
    public bool Suspended { get; set; }
}
