namespace Maran.Modules.ServerMigration.Domain.Policies;

/// <summary>The bounds of a migration job's settings (decision D20: concurrency 1 to 8, default 2; bandwidth per job).</summary>
public static class MigrationJobLimits
{
    /// <summary>The fewest accounts transferred at once.</summary>
    public const int MinConcurrency = 1;

    /// <summary>The most accounts transferred at once.</summary>
    public const int MaxConcurrency = 8;

    /// <summary>How many accounts are transferred at once unless the administrator says otherwise.</summary>
    public const int DefaultConcurrency = 2;

    /// <summary>The largest bandwidth limit, in KiB per second; the agent's own ceiling.</summary>
    public const int MaxBandwidthLimitKib = 1_048_576;

    /// <summary>The most accounts one job holds; the discovery's own ceiling.</summary>
    public const int MaxAccounts = 5000;
}
