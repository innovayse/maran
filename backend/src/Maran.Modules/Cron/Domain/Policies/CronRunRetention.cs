namespace Maran.Modules.Cron.Domain.Policies;

/// <summary>How many runs started by hand the panel keeps for one entry.</summary>
/// <remarks>
/// The issue asks for the RECENT runs, not a log: a history long enough to compare the last few
/// attempts while a job is being fixed. Each row may carry up to 128 KiB of the customer's output, so
/// the bound is also what keeps one entry clicked in a loop from growing the panel's database without
/// limit. Older rows are removed in the same request that adds a new one.
/// </remarks>
public static class CronRunRetention
{
    /// <summary>The most runs kept per entry; the oldest beyond it are removed.</summary>
    public const int RunsPerEntry = 20;
}
