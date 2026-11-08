using System.Globalization;
using Maran.Agent.Client.Services.BackupService;
using Maran.Agent.Client.Services.CronService;
using Maran.Agent.Client.Services.FirewallService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Simulated.Enums;
using Maran.Agent.Simulated.Interfaces;
using Maran.Agent.Simulated.Services.BackupService;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Usage;
using Maran.Host.Demo.World.Enums;
using Maran.Modules.Sites.Domain.Enums;
using Maran.SharedKernel.Results;

namespace Maran.Host.Demo.World;

/// <summary>
/// Gives the simulated server the hosting state the screens read back from it after an API start: the
/// virtual hosts and their certificates, the databases, the SFTP and FTPS logins, the crontabs, the
/// backup artifacts and the firewall's rules and bans. It is built from <see cref="StandardDemoWorld"/>,
/// the definition the database seed uses, and runs after <see cref="DemoWorldAgentSeed"/>, which created
/// the accounts it fills.
/// </summary>
/// <remarks>
/// Every entry goes through the stores' own public methods, so a seed larger than the limits fails
/// here, loudly, and not as a screen that quietly shows fewer rows. The figures the database cannot say
/// (database sizes, certificate dates, backup sizes) are derived from names and the clock, so two starts
/// at the same moment agree.
/// </remarks>
public sealed class DemoWorldHostingSeed : ISimulatedAgentSeed
{
    /// <summary>Bytes in a megabyte, as database sizes count them.</summary>
    private const ulong BytesPerMegabyte = 1024UL * 1024UL;

    /// <summary>The smallest database the seed reports, in megabytes.</summary>
    private const ulong SmallestDatabaseMegabytes = 8;

    /// <summary>How many megabytes above the smallest a seeded database may report.</summary>
    private const ulong DatabaseSpreadMegabytes = 120;

    /// <summary>The port the simulated sshd listens on; the firewall refuses a rule that would close it.</summary>
    private const int SshPort = 22;

    /// <summary>The public port the simulated panel is reachable on.</summary>
    private const int PanelPort = 8443;

    /// <summary>Lowest passive data port of the simulated FTPS daemon.</summary>
    private const uint FtpsPassiveMin = 40000;

    /// <summary>Highest passive data port of the simulated FTPS daemon.</summary>
    private const uint FtpsPassiveMax = 40100;

    /// <summary>Concurrent sessions the simulated FTPS daemon accepts.</summary>
    private const uint FtpsMaxClients = 20;

    /// <summary>Days a freshly issued certificate is valid for.</summary>
    private const int CertificateLifetimeDays = 90;

    /// <summary>Days before expiry at which a certificate is shown as expiring soon.</summary>
    private const int ExpiringSoonDaysLeft = 9;

    /// <summary>Days before expiry at which a certificate whose renewal keeps failing stands.</summary>
    private const int RenewalFailedDaysLeft = 4;

    /// <summary>Days between two seeded backups of one account.</summary>
    private const int DaysBetweenBackups = 1;

    /// <summary>Reads the instant certificates and backups are dated from.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the seed.</summary>
    /// <param name="clock">Reads the instant certificates and backups are dated from.</param>
    public DemoWorldHostingSeed(IClock clock)
    {
        _clock = clock;
    }

    /// <inheritdoc />
    public void ApplyTo(SimulatedAgentLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        var world = StandardDemoWorld.Instance;

        foreach (var account in world.Accounts)
        {
            SeedSites(ledger, account);
            SeedDatabases(ledger, account);
            SeedTransferLogins(ledger, account);
            SeedCron(ledger, account);
            SeedBackups(ledger, account);
        }

        SeedFtpsDaemon(ledger, world);
        SeedFirewall(ledger, world.Firewall);
    }

    /// <summary>Adds an account's virtual hosts and the certificates its sites hold.</summary>
    /// <param name="ledger">The ledger to write to.</param>
    /// <param name="account">The account whose sites are added.</param>
    private void SeedSites(SimulatedAgentLedger ledger, DemoAccountDefinition account)
    {
        var home = "/home/" + account.Name;
        foreach (var site in account.Sites)
        {
            var backend = BackendOf(site.BackendType);
            var isPhp = backend == SiteBackendKind.Php;
            Require(
                account.Name,
                site.Domain,
                ledger.Vhosts.Add(
                    site.Domain,
                    new SimulatedVhost(
                        account.Name,
                        site.Domain,
                        site.Aliases.ToList(),
                        backend,
                        isPhp ? site.PhpVersion : string.Empty,
                        backend == SiteBackendKind.ReverseProxy ? site.ProxyUpstream : string.Empty,
                        isPhp ? 5U : 0U,
                        [],
                        site.Enabled,
                        home + "/sites/" + site.Domain)));

            var daysLeft = site.Certificate switch
            {
                DemoCertificateState.Valid => CertificateLifetimeDays - 30,
                DemoCertificateState.ExpiringSoon => ExpiringSoonDaysLeft,
                DemoCertificateState.RenewalFailed => RenewalFailedDaysLeft,
                _ => 0,
            };
            if (daysLeft > 0)
            {
                var expiresAt = _clock.UtcNow.AddDays(daysLeft);
                Require(
                    account.Name,
                    site.Domain,
                    ledger.Certificates.Add(
                        site.Domain,
                        new SimulatedCertificate(
                            account.Name,
                            site.Domain,
                            expiresAt,
                            expiresAt.AddDays(-CertificateLifetimeDays))));
            }
        }
    }

    /// <summary>Adds an account's databases with the sizes they report.</summary>
    /// <param name="ledger">The ledger to write to.</param>
    /// <param name="account">The account whose databases are added.</param>
    private static void SeedDatabases(SimulatedAgentLedger ledger, DemoAccountDefinition account)
    {
        foreach (var database in account.Databases)
        {
            var megabytes = SmallestDatabaseMegabytes
                + (StableHash.Of(account.Name + "_" + database.Suffix) % DatabaseSpreadMegabytes);
            Require(
                account.Name,
                database.Suffix,
                ledger.Databases.Create(
                    account.Name,
                    database.Suffix,
                    database.UserSuffix,
                    megabytes * BytesPerMegabyte));
        }
    }

    /// <summary>Adds an account's SFTP and FTPS logins, with no session open.</summary>
    /// <param name="ledger">The ledger to write to.</param>
    /// <param name="account">The account whose logins are added.</param>
    private static void SeedTransferLogins(SimulatedAgentLedger ledger, DemoAccountDefinition account)
    {
        foreach (var user in account.SftpUsers)
        {
            Require(account.Name, user, ledger.Logins.Create(account.Name, user, 0));
        }

        foreach (var user in account.FtpUsers)
        {
            Require(account.Name, user, ledger.FtpsLogins.CreateLogin(account.Name, user, 0));
        }
    }

    /// <summary>Adds an account's crontab, with each entry enabled or not as defined.</summary>
    /// <param name="ledger">The ledger to write to.</param>
    /// <param name="account">The account whose crontab is filled.</param>
    private static void SeedCron(SimulatedAgentLedger ledger, DemoAccountDefinition account)
    {
        foreach (var entry in account.CronEntries)
        {
            var schedule = new AgentCronSchedule(
                entry.Schedule.Minute,
                entry.Schedule.Hour,
                entry.Schedule.DayOfMonth,
                entry.Schedule.Month,
                entry.Schedule.DayOfWeek);
            var created = ledger.CronTables.Create(account.Name, schedule, entry.Command, null);
            Require(account.Name, entry.Command, created);
            if (!entry.Enabled)
            {
                Require(
                    account.Name,
                    entry.Command,
                    ledger.CronTables.SetEnabled(account.Name, created.Value, false));
            }
        }

        if (account.State == DemoAccountState.Suspended)
        {
            ledger.CronTables.SetSuspended(account.Name, true);
        }
    }

    /// <summary>Adds an account's backup artifacts, one a day going back from now.</summary>
    /// <param name="ledger">The ledger to write to.</param>
    /// <param name="account">The account whose artifacts are added.</param>
    private void SeedBackups(SimulatedAgentLedger ledger, DemoAccountDefinition account)
    {
        var databases = ledger.Databases.ListFor(account.Name);
        for (var taken = 0; taken < account.BackupCount; taken++)
        {
            var backupId = $"{account.Name}-{taken + 1:D3}";
            var createdAt = _clock.UtcNow.AddDays(-(taken + 1) * DaysBetweenBackups);
            Require(
                account.Name,
                backupId,
                ledger.Backups.GetOrAdd(
                    account.Name,
                    backupId,
                    () =>
                    {
                        return new AgentBackupSummary(
                            backupId,
                            SimulatedBackupFigures.Describe(account.Name, backupId, databases, createdAt),
                            null);
                    }));
        }
    }

    /// <summary>Brings the FTPS daemon up when any account has an FTPS login.</summary>
    /// <param name="ledger">The ledger to write to.</param>
    /// <param name="world">The world whose accounts say whether the daemon is wanted.</param>
    private static void SeedFtpsDaemon(SimulatedAgentLedger ledger, DemoWorld world)
    {
        var featured = world.Accounts.Single(account => { return account.Name == world.FeaturedCustomerAccountName; });
        if (world.Accounts.Any(account => { return account.FtpUsers.Count > 0; }))
        {
            Require(
                featured.Name,
                "ftps",
                ledger.FtpsLogins.Enable(
                    featured.Sites[0].Domain,
                    FtpsPassiveMin,
                    FtpsPassiveMax,
                    string.Empty,
                    FtpsMaxClients));
        }
    }

    /// <summary>Adds the firewall's port rules and bans.</summary>
    /// <param name="ledger">The ledger to write to.</param>
    /// <param name="firewall">The rules and bans the world defines.</param>
    private void SeedFirewall(SimulatedAgentLedger ledger, DemoFirewallDefinition firewall)
    {
        IReadOnlyList<int> sshPorts = [SshPort];
        foreach (var rule in firewall.PortRules.Where(rule => { return rule.Allow; }))
        {
            Require(
                "firewall",
                rule.Port.ToString(CultureInfo.InvariantCulture),
                ledger.FirewallRules.Allow(rule.Port, null, rule.Protocol, rule.SourceCidr, sshPorts, PanelPort));
        }

        foreach (var ban in firewall.Bans)
        {
            AgentBanTerm term = ban.DurationMinutes is { } minutes
                ? new AgentBanTerm.ExpiresAfter(TimeSpan.FromMinutes(minutes))
                : new AgentBanTerm.Permanent();
            Require("firewall", ban.Address, ledger.FirewallRules.Ban(ban.Address, term, _clock.UtcNow));
        }
    }

    /// <summary>Maps the panel's backend kind onto the one the agent speaks.</summary>
    /// <param name="type">The backend kind the world defines.</param>
    /// <returns>The agent's backend kind.</returns>
    private static SiteBackendKind BackendOf(SiteBackendType type)
    {
        return type switch
        {
            SiteBackendType.Php => SiteBackendKind.Php,
            SiteBackendType.ReverseProxy => SiteBackendKind.ReverseProxy,
            _ => SiteBackendKind.Static,
        };
    }

    /// <summary>Fails loudly when the ledger refused an entry the world defines.</summary>
    /// <param name="owner">The account or area the entry belongs to.</param>
    /// <param name="name">The entry's name.</param>
    /// <param name="result">What the ledger answered.</param>
    private static void Require(string owner, string name, Result result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"The simulated server refused '{name}' of '{owner}': {result.Error!.Code}.");
        }
    }

    /// <summary>Fails loudly when the ledger refused an entry the world defines.</summary>
    /// <typeparam name="TValue">What the ledger answered with on success.</typeparam>
    /// <param name="owner">The account or area the entry belongs to.</param>
    /// <param name="name">The entry's name.</param>
    /// <param name="result">What the ledger answered.</param>
    private static void Require<TValue>(string owner, string name, Result<TValue> result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"The simulated server refused '{name}' of '{owner}': {result.Error!.Code}.");
        }
    }

    /// <summary>Fails loudly when the ledger refused an entry the world defines.</summary>
    /// <param name="owner">The account or area the entry belongs to.</param>
    /// <param name="name">The entry's name.</param>
    /// <param name="added">What the ledger answered.</param>
    private static void Require(string owner, string name, SimulatedLedgerAdd added)
    {
        if (added != SimulatedLedgerAdd.Added)
        {
            throw new InvalidOperationException($"The simulated server refused '{name}' of '{owner}': {added}.");
        }
    }
}
