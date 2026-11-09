using Maran.Host.Demo.World.Enums;

namespace Maran.Host.Demo.World;

/// <summary>One hosting account of the demo world and everything that belongs to it.</summary>
/// <param name="Name">The account (system user) name; also its owner's login name.</param>
/// <param name="PrimaryDomain">The account's primary domain.</param>
/// <param name="PlanName">The plan, by the name a plan definition or a standard plan carries.</param>
/// <param name="OwnerEmail">The owner's address; nothing is sent to it in the demo.</param>
/// <param name="OwnerLanguage">The owner's language code.</param>
/// <param name="ResellerName">The owning reseller's name, or null when the administrator owns the account.</param>
/// <param name="State">The state the account is left in.</param>
/// <param name="DiskUsedMb">
/// The disk usage the simulated server reports; above the quota for an over-limit account.
/// </param>
/// <param name="Sites">The account's sites.</param>
/// <param name="Databases">The account's databases.</param>
/// <param name="SftpUsers">Name suffixes of the account's SFTP logins.</param>
/// <param name="FtpUsers">Name suffixes of the account's FTPS logins.</param>
/// <param name="CronEntries">The account's scheduled jobs.</param>
/// <param name="BackupCount">How many backups the account's history holds.</param>
/// <param name="BackupSchedule">The account's backup schedule, or null for none.</param>
public sealed record DemoAccountDefinition(
    string Name,
    string PrimaryDomain,
    string PlanName,
    string OwnerEmail,
    string OwnerLanguage,
    string? ResellerName,
    DemoAccountState State,
    int DiskUsedMb,
    IReadOnlyList<DemoSiteDefinition> Sites,
    IReadOnlyList<DemoDatabaseDefinition> Databases,
    IReadOnlyList<string> SftpUsers,
    IReadOnlyList<string> FtpUsers,
    IReadOnlyList<DemoCronDefinition> CronEntries,
    int BackupCount,
    DemoBackupScheduleDefinition? BackupSchedule);
