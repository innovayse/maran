namespace Maran.Modules.ServerMigration.Domain.Policies;

/// <summary>
/// How much of one discovery the panel is willing to hold, so a hostile or broken source cannot make the panel
/// store or buffer without bound. The agent stops at the same account bound and says so with a warning; reaching
/// these here means the agent did not, and the discovery is refused rather than stored half.
/// </summary>
public static class DiscoveryLimits
{
    /// <summary>The most accounts one discovery may report.</summary>
    public const int MaxAccounts = 5000;

    /// <summary>The most warnings one discovery may report.</summary>
    public const int MaxWarnings = 10000;
}
