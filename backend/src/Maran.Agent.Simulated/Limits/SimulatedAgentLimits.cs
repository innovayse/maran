namespace Maran.Agent.Simulated.Limits;

/// <summary>
/// The caps that keep a simulated host from growing without bound (threat T6 of the public
/// demonstration): every figure is a ceiling on memory a visitor's input can make the process hold.
/// </summary>
/// <remarks>
/// These are not a second rule set. The panel's validators have already judged every value; the caps
/// answer a different question, "how much may the simulation remember", and a value over a cap is
/// refused with the agent's own <c>AgentLimitReached</c> or <c>AgentInvalidInput</c>, the codes the
/// real agent answers with. A record with init properties and no logic, so a test can build a tiny
/// variant and prove each cap is enforced by exceeding it.
/// </remarks>
public sealed record SimulatedAgentLimits
{
    /// <summary>The defaults the demonstration runs with.</summary>
    public static readonly SimulatedAgentLimits Default = new();

    /// <summary>Longest account system user name accepted.</summary>
    public int MaxAccountNameLength { get; init; } = 32;

    /// <summary>Most hosting accounts the simulated host holds.</summary>
    public int MaxAccounts { get; init; } = 200;

    /// <summary>Longest domain or alias accepted (the DNS limit).</summary>
    public int MaxDomainLength { get; init; } = 253;

    /// <summary>Most sites, counted across every account.</summary>
    public int MaxSites { get; init; } = 1000;

    /// <summary>Most aliases one site may carry.</summary>
    public int MaxAliasesPerSite { get; init; } = 20;

    /// <summary>Most php.ini overrides one site may carry.</summary>
    public int MaxSettingOverridesPerSite { get; init; } = 64;

    /// <summary>Longest setting name or value accepted, in characters.</summary>
    public int MaxSettingTextLength { get; init; } = 1024;

    /// <summary>Longest reverse-proxy upstream accepted, in characters.</summary>
    public int MaxProxyUpstreamLength { get; init; } = 512;

    /// <summary>Most historical lines a log tail replays, whatever the caller asked for.</summary>
    public uint MaxLogHistoryLines { get; init; } = 200;

    /// <summary>Largest certificate (with chain) accepted, in bytes.</summary>
    public int MaxCertificatePemBytes { get; init; } = 65536;

    /// <summary>Largest private key accepted, in bytes.</summary>
    public int MaxPrivateKeyPemBytes { get; init; } = 16384;

    /// <summary>Most outstanding challenge files for the panel's own host name.</summary>
    public int MaxPanelChallenges { get; init; } = 64;

    /// <summary>Longest challenge token or key authorization accepted, in characters.</summary>
    public int MaxChallengeTextLength { get; init; } = 1024;

    /// <summary>Most PHP versions installed at once.</summary>
    public int MaxPhpRuntimes { get; init; } = 32;

    /// <summary>Most apps (web servers and runtimes) installed through the app store at once.</summary>
    public int MaxApps { get; init; } = 32;

    /// <summary>The longest account, suffix, backup id or entry id a simulated service accepts.</summary>
    public int MaxNameLength { get; init; } = 64;

    /// <summary>How many databases the simulated server holds across every account.</summary>
    public int MaxDatabases { get; init; } = 5_000;

    /// <summary>How many SFTP logins the simulated server holds across every account.</summary>
    public int MaxSftpLogins { get; init; } = 5_000;

    /// <summary>How many FTPS logins the simulated server holds across every account.</summary>
    public int MaxFtpsLogins { get; init; } = 5_000;

    /// <summary>How many public keys one SFTP login may hold.</summary>
    public int MaxPublicKeysPerLogin { get; init; } = 64;

    /// <summary>The longest single public-key line a login accepts, in characters.</summary>
    public int MaxPublicKeyLength { get; init; } = 8_192;

    /// <summary>The longest relative path a file operation accepts, in characters.</summary>
    public int MaxFilePathLength { get; init; } = 1_024;

    /// <summary>How many file and directory entries the simulated server holds across every account.</summary>
    public int MaxFileEntries { get; init; } = 20_000;

    /// <summary>How many bytes of file content one account may hold.</summary>
    public long MaxFileBytesPerAccount { get; init; } = 8L * 1024 * 1024;

    /// <summary>How many bytes of file content the whole simulated server may hold.</summary>
    public long MaxFileBytesTotal { get; init; } = 64L * 1024 * 1024;

    /// <summary>How many cron entries the simulated server holds across every account.</summary>
    public int MaxCronEntriesTotal { get; init; } = 5_000;

    /// <summary>How many cron entries one account may hold whatever its plan says.</summary>
    public int MaxCronEntriesPerAccount { get; init; } = 200;

    /// <summary>The longest cron command line, in characters.</summary>
    public int MaxCronCommandLength { get; init; } = 4_096;

    /// <summary>How many managed environment assignments one crontab may hold.</summary>
    public int MaxCronEnvironmentVariables { get; init; } = 64;

    /// <summary>The longest environment variable name, in characters.</summary>
    public int MaxCronEnvironmentNameLength { get; init; } = 128;

    /// <summary>The longest environment variable value, in characters.</summary>
    public int MaxCronEnvironmentValueLength { get; init; } = 4_096;

    /// <summary>How many port rules the simulated firewall holds.</summary>
    public int MaxFirewallRules { get; init; } = 1_000;

    /// <summary>How many address bans the simulated firewall holds.</summary>
    public int MaxFirewallBans { get; init; } = 10_000;

    /// <summary>How many backup artifacts the simulated server holds across every account.</summary>
    public int MaxBackupsTotal { get; init; } = 2_000;

    /// <summary>How many backup artifacts one account may hold.</summary>
    public int MaxBackupsPerAccount { get; init; } = 50;
}
