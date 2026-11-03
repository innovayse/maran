using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Cron.Options;

/// <summary>
/// How many cron runs started by hand may be in flight at once. Bound from the <c>Cron:Runs</c>
/// configuration section, <c>Cron__Runs__*</c> in <c>panel.env</c> (issue #333).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the panel bounds this and not only the agent.</b> A run holds one of the agent's worker
/// threads for as long as the job lives — up to its runtime ceiling of a few minutes — and the only
/// other limit on <c>POST /cron-entries/{id}/runs</c> is the generic per-account request rate, which
/// is far above what the agent can hold open at once. The agent bounds its own pool; this bound
/// refuses the request BEFORE the agent is called, so a customer who clicks "run" in a loop is
/// answered at once by the panel and never queues on the root process.
/// </para>
/// <para>
/// Both numbers are counted over runs that are IN FLIGHT in this panel process, not over a rate: a
/// slot is taken when the handler is about to call the agent and given back when the handler is
/// done with the run, whether it answered, was refused or failed. They are settings because a host with a handful of accounts and one with
/// thousands want different ceilings, and they are validated at startup so a value that would
/// silently disable the protection refuses the boot instead (rules/csharp.md "Options validated at
/// startup").
/// </para>
/// </remarks>
public sealed class CronRunOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Cron:Runs";

    /// <summary>The most runs one account may have in flight at once.</summary>
    /// <remarks>
    /// Two: a customer testing a job wants to run it, read it, and run it again, and a second slot
    /// covers a run of a second entry while the first is still going. More is only a way to hold
    /// the agent's threads.
    /// </remarks>
    [Range(1, 1_000)]
    public int MaxConcurrentPerAccount { get; set; } = 2;

    /// <summary>The most runs the whole panel may have in flight at once, across every account.</summary>
    /// <remarks>
    /// Sixteen: far below the agent's own worker pool, so that cron runs alone can never be the
    /// reason the agent has no thread left for the rest of the panel. A value below
    /// <see cref="MaxConcurrentPerAccount"/> is allowed and makes the per-account bound unreachable,
    /// which is the operator's call to make.
    /// </remarks>
    [Range(1, 10_000)]
    public int MaxConcurrentGlobal { get; set; } = 16;
}
