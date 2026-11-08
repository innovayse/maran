using Maran.Host.Demo.Seeding.Steps;
using Maran.Host.Demo.World;
using Maran.Host.Demo.World.Enums;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Persistence;
using Maran.SharedKernel.Callers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Seeding.Services;

/// <summary>
/// Reads the database back and compares it, by value, with the world it was built from. A seed that
/// ran without error but left a screen empty fails here instead of in front of a visitor.
/// </summary>
/// <remarks>
/// It observes the database only. What the simulated server holds is rebuilt from the same world at
/// every start of the API and cannot be read from this process.
/// </remarks>
public sealed class DemoSeedVerifier
{
    /// <summary>Opens the scope the contexts are resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Switches the caller, so every tenant-scoped table is readable.</summary>
    private readonly DemoSeedCallerAccessor _caller;

    /// <summary>The published sign-ins.</summary>
    private readonly DemoIdentityOptions _identities;

    /// <summary>Creates the verifier.</summary>
    /// <param name="scopeFactory">Opens the scope the contexts are resolved in.</param>
    /// <param name="caller">Switches the caller.</param>
    /// <param name="identities">The published sign-ins.</param>
    public DemoSeedVerifier(
        IServiceScopeFactory scopeFactory,
        DemoSeedCallerAccessor caller,
        IOptions<DemoIdentityOptions> identities)
    {
        _scopeFactory = scopeFactory;
        _caller = caller;
        _identities = identities.Value;
    }

    /// <summary>Compares the database with the world.</summary>
    /// <param name="world">The world the database should hold.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>One sentence per difference; empty when the database holds the world.</returns>
    public async Task<IReadOnlyList<string>> VerifyAsync(DemoWorld world, CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var identity = services.GetRequiredService<IdentityDbContext>();

        var administrator = await identity.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Username == _identities.AdministratorUsername, cancellationToken);
        if (administrator is null || administrator.Role != UserRole.Admin)
        {
            problems.Add("the published administrator does not exist");
            return problems;
        }

        using var acting = _caller.Act(new AdministratorCaller(administrator.Id, administrator.Username));

        await VerifyIdentityAsync(identity, world, problems, cancellationToken);
        await VerifyAccountsAsync(services, world, problems, cancellationToken);

        return problems;
    }

    /// <summary>Checks the security policy and the featured customer's login.</summary>
    /// <param name="identity">The identity context.</param>
    /// <param name="world">The world.</param>
    /// <param name="problems">Collects the differences.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>A task that completes when the checks are done.</returns>
    private static async Task VerifyIdentityAsync(
        IdentityDbContext identity,
        DemoWorld world,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        var policy = await identity.SecurityPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == SecurityPolicy.SingletonId, cancellationToken);
        if (policy is null
            || policy.ForceTwoFactorForAdmins
            || policy.ForceTwoFactorForResellers
            || policy.MaxFailedLoginAttempts != SecurityPolicySeedStep.NeverLockedAttempts)
        {
            problems.Add("the security policy is not the demonstration's (two-factor off, no lockout)");
        }

        var customer = await identity.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Username == world.FeaturedCustomerAccountName, cancellationToken);
        if (customer is null || customer.Lifecycle != UserLifecycle.Active)
        {
            problems.Add("the featured customer's login is missing or not active");
        }
    }

    /// <summary>Checks every account and what belongs to it.</summary>
    /// <param name="services">The scope's provider.</param>
    /// <param name="world">The world.</param>
    /// <param name="problems">Collects the differences.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>A task that completes when the checks are done.</returns>
    private static async Task VerifyAccountsAsync(
        IServiceProvider services,
        DemoWorld world,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        var accounts = await services.GetRequiredService<AccountsDbContext>().Accounts
            .AsNoTracking()
            .ToDictionaryAsync(account => { return account.Name; }, StringComparer.Ordinal, cancellationToken);

        var sites = await services.GetRequiredService<SitesDbContext>().Sites
            .AsNoTracking().GroupBy(row => row.AccountId)
            .Select(group => new { AccountId = group.Key, Rows = group.Count() })
            .ToDictionaryAsync(count => { return count.AccountId; }, count => { return count.Rows; }, cancellationToken);
        var databases = await services.GetRequiredService<DatabasesDbContext>().Databases
            .AsNoTracking().GroupBy(row => row.AccountId)
            .Select(group => new { AccountId = group.Key, Rows = group.Count() })
            .ToDictionaryAsync(count => { return count.AccountId; }, count => { return count.Rows; }, cancellationToken);
        var sftp = await services.GetRequiredService<SftpDbContext>().SftpUsers
            .AsNoTracking().GroupBy(row => row.AccountId)
            .Select(group => new { AccountId = group.Key, Rows = group.Count() })
            .ToDictionaryAsync(count => { return count.AccountId; }, count => { return count.Rows; }, cancellationToken);
        var ftp = await services.GetRequiredService<FtpDbContext>().FtpUsers
            .AsNoTracking().GroupBy(row => row.AccountId)
            .Select(group => new { AccountId = group.Key, Rows = group.Count() })
            .ToDictionaryAsync(count => { return count.AccountId; }, count => { return count.Rows; }, cancellationToken);
        var backups = await services.GetRequiredService<BackupsDbContext>().Backups
            .AsNoTracking().GroupBy(row => row.AccountId)
            .Select(group => new { AccountId = group.Key, Rows = group.Count() })
            .ToDictionaryAsync(count => { return count.AccountId; }, count => { return count.Rows; }, cancellationToken);
        var certified = await services.GetRequiredService<SslDbContext>().Certificates
            .AsNoTracking().Select(row => row.Domain)
            .ToListAsync(cancellationToken);

        foreach (var expected in world.Accounts)
        {
            if (!accounts.TryGetValue(expected.Name, out var account))
            {
                problems.Add($"account '{expected.Name}' is missing");
                continue;
            }

            var status = expected.State == DemoAccountState.Suspended ? AccountStatus.Suspended : AccountStatus.Active;
            Expect(problems, account.Status == status, $"account '{expected.Name}' is not {status}");
            Expect(problems, Count(sites, account.Id) == expected.Sites.Count, $"account '{expected.Name}' site count");
            Expect(
                problems,
                Count(databases, account.Id) == expected.Databases.Count,
                $"account '{expected.Name}' database count");
            Expect(
                problems,
                Count(sftp, account.Id) == expected.SftpUsers.Count,
                $"account '{expected.Name}' SFTP login count");
            Expect(
                problems,
                Count(ftp, account.Id) == expected.FtpUsers.Count,
                $"account '{expected.Name}' FTPS login count");
            Expect(
                problems,
                Count(backups, account.Id) == expected.BackupCount,
                $"account '{expected.Name}' backup count");
        }

        var expectedCertificates = world.Sites
            .Where(site => { return site.Enabled && site.Certificate != DemoCertificateState.None; })
            .Select(site => { return site.Domain; })
            .Order(StringComparer.Ordinal);
        Expect(
            problems,
            expectedCertificates.SequenceEqual(certified.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "the certified domains differ from the world's");
    }

    /// <summary>Reads a count from a grouped result, zero when the account has no rows.</summary>
    /// <param name="counts">Counts by account id.</param>
    /// <param name="accountId">The account.</param>
    /// <returns>The count.</returns>
    private static int Count(Dictionary<Guid, int> counts, Guid accountId)
    {
        return counts.GetValueOrDefault(accountId);
    }

    /// <summary>Records a problem when a check failed.</summary>
    /// <param name="problems">Collects the differences.</param>
    /// <param name="holds">Whether the check held.</param>
    /// <param name="problem">The sentence to record when it did not.</param>
    private static void Expect(List<string> problems, bool holds, string problem)
    {
        if (!holds)
        {
            problems.Add(problem);
        }
    }
}
