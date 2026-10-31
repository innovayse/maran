using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The scale dataset issue #196 measures every list endpoint over: more than a thousand hosting
/// accounts, each with the rows a real tenant accumulates. This half writes the tenant-owned rows —
/// plans, accounts, sites with their certificates, databases and file-transfer logins;
/// <see cref="ScaleJournalDataset"/> writes the journals that grow with them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reproducible by construction.</b> Every id is derived from a row kind and an index
/// (<see cref="IdOf"/>), every name from the account index, and every instant from
/// <see cref="Epoch"/> plus a fixed step — nothing reads a clock or a random source. Two runs build
/// byte-identical tables, so a plan or a timing recorded against one is a statement about the other.
/// </para>
/// <para>
/// <b>Written through the module contexts, not through SQL.</b> The rows come from the entities' own
/// constructors and domain methods, so a seeded site claims its host names exactly as a created one
/// does and a seeded backup carries the state a finished one carries; a hand-written INSERT would
/// measure a table the panel can never produce. Change tracking is switched off for the bulk add —
/// it is the cost of the seed, not of the panel.
/// </para>
/// </remarks>
public static class ScaleDataset
{
    /// <summary>How many hosting accounts the dataset holds — above the thousand the issue names.</summary>
    public const int AccountCount = 1_200;

    /// <summary>How many plans the accounts are spread across.</summary>
    public const int PlanCount = 24;

    /// <summary>How many sites each account holds; each site carries one certificate.</summary>
    public const int SitesPerAccount = 2;

    /// <summary>How many databases each account holds.</summary>
    public const int DatabasesPerAccount = 2;

    /// <summary>Every how-many-th account is suspended, so the status filter selects a real subset.</summary>
    public const int SuspendedEvery = 10;

    /// <summary>The instant every seeded timestamp is measured from.</summary>
    public static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The PHP versions sites are spread across, so the version filter selects a subset.</summary>
    private static readonly string[] PhpVersions = ["8.1", "8.2", "8.3"];

    /// <summary>Writes the tenant-owned half of the dataset into an already migrated panel.</summary>
    /// <param name="services">A scope's provider, outside any request, so no tenant filter applies.</param>
    /// <returns>A task that completes when every row is committed.</returns>
    public static async Task SeedAsync(IServiceProvider services)
    {
        var accounts = await SeedAccountsAsync(services.GetRequiredService<AccountsDbContext>());
        await SeedSitesAsync(
            services.GetRequiredService<SitesDbContext>(), services.GetRequiredService<SslDbContext>(), accounts);
        await SeedDatabasesAsync(services.GetRequiredService<DatabasesDbContext>(), accounts);
        await SeedLoginsAsync(
            services.GetRequiredService<SftpDbContext>(), services.GetRequiredService<FtpDbContext>(), accounts);
    }

    /// <summary>The name of the account at <paramref name="index"/>.</summary>
    /// <param name="index">The account's position, from zero.</param>
    /// <returns>A valid account name, sortable in index order.</returns>
    public static string AccountName(int index)
    {
        return $"acct{index:D4}";
    }

    /// <summary>A deterministic id for the <paramref name="index"/>-th row of one kind.</summary>
    /// <param name="kind">Distinguishes the row kinds, so two kinds never share an id.</param>
    /// <param name="index">The row's position within its kind.</param>
    /// <returns>The id.</returns>
    public static Guid IdOf(short kind, int index)
    {
        return new Guid(index, kind, 0x0196, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>Stores the plans and the accounts, every tenth account suspended.</summary>
    /// <param name="context">The Accounts module's context.</param>
    /// <returns>Every account's id and name, in index order.</returns>
    private static async Task<IReadOnlyList<(Guid Id, string Name)>> SeedAccountsAsync(AccountsDbContext context)
    {
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        var plans = Enumerable.Range(0, PlanCount)
            .Select(index =>
            {
                return Plan.Define(IdOf(1, index), $"Scale plan {index:D2}", 1_024 * (index + 1), 10, 10, 10, 10, 10, 5);
            })
            .ToList();
        context.Plans.AddRange(plans);

        var accounts = new List<(Guid Id, string Name)>(AccountCount);
        for (var index = 0; index < AccountCount; index++)
        {
            var name = AccountName(index);
            var account = new Account(
                IdOf(2, index), name, $"{name}.example.com", plans[index % PlanCount].Id, Epoch.AddMinutes(index));
            if (index % SuspendedEvery == 0)
            {
                account.Suspend();
            }

            context.Accounts.Add(account);
            accounts.Add((account.Id, name));
        }

        await context.SaveChangesAsync();
        return accounts;
    }

    /// <summary>Stores each account's sites, and one certificate per site.</summary>
    /// <param name="sites">The Sites module's context.</param>
    /// <param name="ssl">The Ssl module's context.</param>
    /// <param name="accounts">The accounts, in index order.</param>
    /// <returns>A task that completes when both contexts are committed.</returns>
    /// <remarks>
    /// Certificate expiries are spread a week apart from a year before <see cref="Epoch"/> onward, so
    /// expired, valid and soon-expiring certificates all exist whatever day the suite runs on.
    /// </remarks>
    private static async Task SeedSitesAsync(
        SitesDbContext sites,
        SslDbContext ssl,
        IReadOnlyList<(Guid Id, string Name)> accounts)
    {
        sites.ChangeTracker.AutoDetectChangesEnabled = false;
        ssl.ChangeTracker.AutoDetectChangesEnabled = false;

        for (var index = 0; index < accounts.Count * SitesPerAccount; index++)
        {
            var (accountId, name) = accounts[index / SitesPerAccount];
            var domain = $"site{index % SitesPerAccount}.{name}.example.com";
            var site = new Site(
                IdOf(3, index),
                accountId,
                domain,
                [],
                SiteBackendType.Php,
                PhpVersions[index % PhpVersions.Length],
                string.Empty,
                $"/home/{name}/sites/{domain}",
                Epoch.AddMinutes(index));
            sites.Sites.Add(site);

            ssl.Certificates.Add(new Certificate(
                IdOf(4, index),
                accountId,
                site.Id,
                domain,
                CertificateSource.Acme,
                Epoch.AddDays(-365 + (index % 300 * 7)),
                Epoch.AddMinutes(index)));
        }

        await sites.SaveChangesAsync();
        await ssl.SaveChangesAsync();
    }

    /// <summary>Stores each account's databases under the account's prefix.</summary>
    /// <param name="context">The Databases module's context.</param>
    /// <param name="accounts">The accounts, in index order.</param>
    /// <returns>A task that completes when the rows are committed.</returns>
    private static async Task SeedDatabasesAsync(
        DatabasesDbContext context,
        IReadOnlyList<(Guid Id, string Name)> accounts)
    {
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        for (var index = 0; index < accounts.Count * DatabasesPerAccount; index++)
        {
            var (accountId, name) = accounts[index / DatabasesPerAccount];
            var shortName = $"db{index % DatabasesPerAccount}";
            context.Databases.Add(new Database(
                IdOf(5, index), accountId, shortName, $"{name}_{shortName}", $"{name}_{shortName}", shortName,
                Epoch.AddMinutes(index)));
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Stores one SFTP login and one FTP login per account.</summary>
    /// <param name="sftp">The Sftp module's context.</param>
    /// <param name="ftp">The Ftp module's context.</param>
    /// <param name="accounts">The accounts, in index order.</param>
    /// <returns>A task that completes when both contexts are committed.</returns>
    private static async Task SeedLoginsAsync(
        SftpDbContext sftp,
        FtpDbContext ftp,
        IReadOnlyList<(Guid Id, string Name)> accounts)
    {
        sftp.ChangeTracker.AutoDetectChangesEnabled = false;
        ftp.ChangeTracker.AutoDetectChangesEnabled = false;

        for (var index = 0; index < accounts.Count; index++)
        {
            var (accountId, name) = accounts[index];
            sftp.SftpUsers.Add(new SftpUser(IdOf(6, index), accountId, "deploy", $"{name}_deploy", Epoch.AddMinutes(index)));
            ftp.FtpUsers.Add(new FtpUser(IdOf(7, index), accountId, "upload", $"{name}_upload", Epoch.AddMinutes(index)));
        }

        await sftp.SaveChangesAsync();
        await ftp.SaveChangesAsync();
    }
}
