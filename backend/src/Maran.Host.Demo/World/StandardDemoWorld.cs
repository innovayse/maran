using Maran.Agent.Client.Services.FirewallService;
using Maran.Host.Demo.World.Enums;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Cron.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Host.Demo.World;

/// <summary>
/// The demonstration's content: accounts in every state a screen shows, sites on several PHP versions,
/// certificates valid, expiring and failed to renew, databases, file-transfer logins, scheduled jobs
/// with runs, backups with a history and a schedule, firewall rules and bans, and a reseller with its
/// own plans and accounts. Every host name sits under <c>example.com</c> and every address in a
/// documentation range, so nothing here names anything real.
/// </summary>
public static class StandardDemoWorld
{
    /// <summary>The name of the standard starter plan, as accounts refer to it.</summary>
    public const string StarterPlanName = "Starter";

    /// <summary>The name of the standard business plan, as accounts refer to it.</summary>
    public const string BusinessPlanName = "Business";

    /// <summary>The name of the standard unlimited plan, as accounts refer to it.</summary>
    public const string UnlimitedPlanName = "Unlimited";

    /// <summary>The account whose owner login is the published customer credential.</summary>
    public const string FeaturedCustomerAccount = "acmebakery";

    /// <summary>The name of the plan the administrator adds.</summary>
    private const string AgencyPlanName = "Agency";

    /// <summary>The name of the plan the reseller owns.</summary>
    private const string ResellerPlanName = "Bright small";

    /// <summary>The reseller's name.</summary>
    private const string ResellerName = "brightnet";

    /// <summary>The features every plan of the world sells.</summary>
    private static readonly IReadOnlyList<AccountFeature> AllFeatures =
    [
        AccountFeature.Databases,
        AccountFeature.Cron,
        AccountFeature.FileTransfer,
        AccountFeature.Backups,
        AccountFeature.Sites,
        AccountFeature.Ssl,
        AccountFeature.Tasks,
        AccountFeature.Php,
        AccountFeature.Files,
    ];

    /// <summary>The disk quotas, in megabytes, of the three standard plans the plan seeder creates.</summary>
    private static readonly Dictionary<string, int> StandardQuotasMb =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [StarterPlanName] = 5_120,
            [BusinessPlanName] = 25_600,
            [UnlimitedPlanName] = 1_048_576,
        };

    /// <summary>The world, built once.</summary>
    public static readonly DemoWorld Instance = Build();

    /// <summary>The disk quota of the plan an account is on.</summary>
    /// <param name="account">An account of <see cref="Instance"/>.</param>
    /// <returns>The quota in megabytes.</returns>
    public static int QuotaMbOf(DemoAccountDefinition account)
    {
        if (StandardQuotasMb.TryGetValue(account.PlanName, out var standard))
        {
            return standard;
        }

        return Instance.Plans
            .Concat(Instance.Resellers.SelectMany(reseller => { return reseller.Plans; }))
            .Single(plan => { return plan.Name == account.PlanName; })
            .DiskQuotaMb;
    }

    /// <summary>Builds the world.</summary>
    /// <returns>The complete definition.</returns>
    private static DemoWorld Build()
    {
        return new DemoWorld(
            FeaturedCustomerAccount,
            [
                new DemoPlanDefinition(AgencyPlanName, 51_200, 50, 20, 20, 20, 40, 10, AllFeatures),
            ],
            [BuildReseller()],
            [
                BuildBakery(),
                BuildNorthwind(),
                BuildGreenleaf(),
                BuildHarbor(),
                ResellerClient("brightclient1", "client1.brightnet.example.com", DemoAccountState.Active, 640),
                ResellerClient("brightclient2", "client2.brightnet.example.com", DemoAccountState.Active, 310),
            ],
            new DemoFirewallDefinition(
                [
                    new DemoPortRuleDefinition(8080, AgentFirewallProtocol.Tcp, "0.0.0.0/0", true),
                    new DemoPortRuleDefinition(5432, AgentFirewallProtocol.Tcp, "203.0.113.0/24", true),
                    new DemoPortRuleDefinition(3306, AgentFirewallProtocol.Tcp, "0.0.0.0/0", false),
                ],
                [
                    new DemoBanDefinition("198.51.100.23", 1_440),
                    new DemoBanDefinition("198.51.100.77", null),
                ],
                [new DemoWhitelistDefinition("203.0.113.0/24", "Office network")]),
            DiskAlertPercent: 85,
            CpuAlertPercent: 90,
            MemoryAlertPercent: 90);
    }

    /// <summary>Builds the reseller.</summary>
    /// <returns>The reseller with its plan.</returns>
    private static DemoResellerDefinition BuildReseller()
    {
        return new DemoResellerDefinition(
            ResellerName,
            "owner@brightnet.example.com",
            new ResellerEnvelopeDto(
                MaxAccounts: 10,
                DiskPoolMb: 51_200,
                MaxPlans: 3,
                MaxDiskQuotaMb: 10_240,
                MaxSites: 10,
                MaxDatabases: 5,
                MaxSftpUsers: 5,
                MaxFtpUsers: 5,
                MaxCronEntries: 10,
                MaxPhpWorkersPerPool: 5,
                MaxMailboxes: 10,
                MailboxQuotaPoolMib: 10_240,
                Features: AllFeatures,
                CpuQuotaPercent: new UnlimitedResourceCeilingDto(),
                MemoryMaxMb: new UnlimitedResourceCeilingDto(),
                MaxProcesses: new UnlimitedResourceCeilingDto()),
            [new DemoPlanDefinition(ResellerPlanName, 2_048, 3, 2, 2, 2, 3, 3, AllFeatures)]);
    }

    /// <summary>Builds the featured customer: a populated account on the business plan.</summary>
    /// <returns>The account.</returns>
    private static DemoAccountDefinition BuildBakery()
    {
        return new DemoAccountDefinition(
            FeaturedCustomerAccount,
            "acmebakery.example.com",
            BusinessPlanName,
            "owner@acmebakery.example.com",
            "en",
            null,
            DemoAccountState.Active,
            DiskUsedMb: 7_800,
            Sites:
            [
                Php("acmebakery.example.com", "8.3", DemoCertificateState.Valid),
                Php("shop.acmebakery.example.com", "8.2", DemoCertificateState.ExpiringSoon),
                Php("legacy.acmebakery.example.com", "8.1", DemoCertificateState.RenewalFailed),
                new DemoSiteDefinition(
                    "blog.acmebakery.example.com", SiteBackendType.Static, string.Empty, string.Empty, [],
                    DemoCertificateState.None, true),
                new DemoSiteDefinition(
                    "staging.acmebakery.example.com", SiteBackendType.Php, "8.3", string.Empty, [],
                    DemoCertificateState.None, false),
            ],
            Databases: [new DemoDatabaseDefinition("shop", "shop"), new DemoDatabaseDefinition("blog", "blog")],
            SftpUsers: ["deploy", "design"],
            FtpUsers: ["assets"],
            CronEntries:
            [
                new DemoCronDefinition(
                    Schedule("*/15", "*", "*", "*", "*"),
                    "php /home/acmebakery/shop/cron.php",
                    true,
                    true),
                new DemoCronDefinition(
                    Schedule("30", "2", "*", "*", "*"),
                    "php /home/acmebakery/shop/cleanup.php",
                    true,
                    true),
                new DemoCronDefinition(
                    Schedule("0", "6", "*", "*", "1"),
                    "php /home/acmebakery/shop/report.php",
                    false,
                    false),
            ],
            BackupCount: 4,
            BackupSchedule: new DemoBackupScheduleDefinition(BackupFrequency.Daily, 3, null, 7));
    }

    /// <summary>Builds the over-limit account: its disk use is above its plan's quota.</summary>
    /// <returns>The account.</returns>
    private static DemoAccountDefinition BuildNorthwind()
    {
        return new DemoAccountDefinition(
            "northwindco",
            "northwindco.example.com",
            StarterPlanName,
            "owner@northwindco.example.com",
            "en",
            null,
            DemoAccountState.Active,
            DiskUsedMb: 5_900,
            Sites:
            [
                Php("northwindco.example.com", "8.2", DemoCertificateState.Valid),
                Php("tools.northwindco.example.com", "8.3", DemoCertificateState.None),
            ],
            Databases: [new DemoDatabaseDefinition("main", "main")],
            SftpUsers: ["upload"],
            FtpUsers: [],
            CronEntries: [new DemoCronDefinition(
                Schedule("0", "*", "*", "*", "*"),
                "php /home/northwindco/tools/sync.php",
                true,
                true)],
            BackupCount: 2,
            BackupSchedule: new DemoBackupScheduleDefinition(BackupFrequency.Weekly, 4, DayOfWeek.Sunday, 4));
    }

    /// <summary>Builds the suspended account.</summary>
    /// <returns>The account.</returns>
    private static DemoAccountDefinition BuildGreenleaf()
    {
        return new DemoAccountDefinition(
            "greenleaf",
            "greenleaf.example.com",
            BusinessPlanName,
            "owner@greenleaf.example.com",
            "ru",
            null,
            DemoAccountState.Suspended,
            DiskUsedMb: 1_200,
            Sites: [Php("greenleaf.example.com", "8.1", DemoCertificateState.Valid)],
            Databases: [new DemoDatabaseDefinition("garden", "garden")],
            SftpUsers: [],
            FtpUsers: [],
            CronEntries: [],
            BackupCount: 1,
            BackupSchedule: null);
    }

    /// <summary>Builds the account on the administrator's own plan with a reverse-proxy site.</summary>
    /// <returns>The account.</returns>
    private static DemoAccountDefinition BuildHarbor()
    {
        return new DemoAccountDefinition(
            "harborstudio",
            "harborstudio.example.com",
            AgencyPlanName,
            "owner@harborstudio.example.com",
            "hy",
            null,
            DemoAccountState.Active,
            DiskUsedMb: 15_400,
            Sites:
            [
                new DemoSiteDefinition(
                    "app.harborstudio.example.com", SiteBackendType.ReverseProxy, string.Empty, "127.0.0.1:3000", [],
                    DemoCertificateState.Valid, true),
                Php("harborstudio.example.com", "8.4", DemoCertificateState.Valid),
            ],
            Databases:
            [
                new DemoDatabaseDefinition("studio", "studio"),
                new DemoDatabaseDefinition("analytics", "stats"),
            ],
            SftpUsers: ["team"],
            FtpUsers: ["media"],
            CronEntries: [new DemoCronDefinition(
                Schedule("*/5", "*", "*", "*", "*"),
                "node /home/harborstudio/app/queue.js",
                true,
                true)],
            BackupCount: 3,
            BackupSchedule: new DemoBackupScheduleDefinition(BackupFrequency.Daily, 1, null, 14));
    }

    /// <summary>Builds one account of the reseller.</summary>
    /// <param name="name">The account name.</param>
    /// <param name="domain">The primary domain.</param>
    /// <param name="state">The state to leave it in.</param>
    /// <param name="diskUsedMb">The disk usage to report.</param>
    /// <returns>The account.</returns>
    private static DemoAccountDefinition ResellerClient(
        string name,
        string domain,
        DemoAccountState state,
        int diskUsedMb)
    {
        return new DemoAccountDefinition(
            name,
            domain,
            ResellerPlanName,
            $"owner@{domain}",
            "en",
            ResellerName,
            state,
            diskUsedMb,
            Sites: [Php(domain, "8.3", DemoCertificateState.Valid)],
            Databases: [new DemoDatabaseDefinition("site", "site")],
            SftpUsers: ["web"],
            FtpUsers: [],
            CronEntries: [],
            BackupCount: 1,
            BackupSchedule: null);
    }

    /// <summary>Builds an enabled PHP site.</summary>
    /// <param name="domain">The site's host name.</param>
    /// <param name="version">The PHP version.</param>
    /// <param name="certificate">The certificate state.</param>
    /// <returns>The site.</returns>
    private static DemoSiteDefinition Php(string domain, string version, DemoCertificateState certificate)
    {
        return new DemoSiteDefinition(domain, SiteBackendType.Php, version, string.Empty, [], certificate, true);
    }

    /// <summary>Builds a schedule from its five fields.</summary>
    /// <param name="minute">Minute field.</param>
    /// <param name="hour">Hour field.</param>
    /// <param name="dayOfMonth">Day-of-month field.</param>
    /// <param name="month">Month field.</param>
    /// <param name="dayOfWeek">Day-of-week field.</param>
    /// <returns>The schedule.</returns>
    private static CronScheduleDto Schedule(
        string minute,
        string hour,
        string dayOfMonth,
        string month,
        string dayOfWeek)
    {
        return new CronScheduleDto(minute, hour, dayOfMonth, month, dayOfWeek);
    }
}
