using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Services.SslService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Jobs;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The unattended certificate renewal pass against a real PostgreSQL, the REAL account directory and
/// the real fail-closed Accounts filter, run as the anonymous caller a scheduled message handler has
/// (issue #379).
/// </summary>
/// <remarks>
/// Why this is an integration test: the defect it pins was invisible to every handler test, because
/// those use a directory double that answers by id and applies no caller check. The real directory
/// refuses an anonymous caller before it reads a row, so every renewal recorded AccountNotFound. Only
/// the ACME authority, the agent and the Sites window are replaced here, because they are the parties
/// a test cannot run; the account lookup, the Accounts schema and the renewal handler are the shipped
/// ones.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class CertificateRenewalAnonymousCallerTests : IAsyncLifetime
{
    /// <summary>The domain whose certificate is due.</summary>
    private const string Domain = "renew.example.com";

    /// <summary>The system user name of the seeded account.</summary>
    private const string Username = "renewacct";

    /// <summary>The plan the seeded account is created against.</summary>
    private static readonly Guid PlanId = new("22222222-0000-4000-8000-0000000003a1");

    /// <summary>The instant every clock in this test reports.</summary>
    private static readonly DateTimeOffset Now = new FixedInstantClock().UtcNow;

    /// <summary>The PostgreSQL this class runs against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public CertificateRenewalAnonymousCallerTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Creates this test's database and applies the Accounts and Ssl migrations.</summary>
    /// <returns>Resolves once both schemas exist.</returns>
    public async Task InitializeAsync()
    {
        await _pg.CreateAsync();
        await using var accounts = OpenAccounts();
        await accounts.Database.MigrateAsync();
        await using var ssl = OpenSsl();
        await ssl.Database.MigrateAsync();
    }

    /// <summary>Releases nothing; the shared server outlives the test.</summary>
    /// <returns>A completed task.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A due certificate of an existing account renews for an anonymous caller and records no AccountNotFound.</summary>
    [Fact]
    public async Task A_due_certificate_renews_with_no_principal_and_records_no_account_not_found()
    {
        var world = await SeedAsync();
        var run = Run(world);

        var renewed = await run.Handler.HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(1, renewed);
        await using var ssl = OpenSsl();
        var stored = await ssl.Certificates.IgnoreQueryFilters().SingleAsync();
        Assert.NotEqual("AccountNotFound", stored.LastRenewalFailureCode);
        Assert.Null(stored.LastRenewalFailureCode);
    }

    /// <summary>The renewal orders and installs the certificate for the account's own system user.</summary>
    [Fact]
    public async Task The_renewal_orders_and_installs_for_the_accounts_own_system_user()
    {
        var world = await SeedAsync();
        var run = Run(world);

        await run.Handler.HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        var order = Assert.Single(run.Acme.Orders);
        Assert.Equal(Domain, order.Domain);
        Assert.Equal(Username, Assert.IsType<SiteDocumentRootChallenge>(order.Challenge).AccountUsername);
        var install = Assert.Single(run.Agent.Installs);
        Assert.Equal(Username, install.Account);
        Assert.Equal(Domain, install.Domain);
        Assert.Equal(1, run.AgentSites.Reloads);
    }

    /// <summary>
    /// The scoped lookup still answers nothing for the same anonymous caller and the same account.
    /// </summary>
    /// <remarks>
    /// The inverse control: it proves the account really is there and that the renewal above succeeded
    /// through the unscoped window, not because the scoped lookup was widened.
    /// </remarks>
    [Fact]
    public async Task The_scoped_lookup_still_answers_null_for_the_same_anonymous_caller_and_account()
    {
        var world = await SeedAsync();
        await using var accounts = OpenAccounts();
        var directory = new AccountDirectory(accounts, new NoPrincipal());

        Assert.Null(await directory.FindAsync(world.AccountId, CancellationToken.None));
        Assert.NotNull(await directory.FindSystemUserAsync(world.AccountId, CancellationToken.None));
    }

    /// <summary>A certificate whose account no longer exists still records AccountNotFound.</summary>
    [Fact]
    public async Task A_certificate_whose_account_does_not_exist_still_records_account_not_found()
    {
        var world = await SeedAsync(accountExists: false);
        var run = Run(world);

        var renewed = await run.Handler.HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(0, renewed);
        await using var ssl = OpenSsl();
        var stored = await ssl.Certificates.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("AccountNotFound", stored.LastRenewalFailureCode);
        Assert.Empty(run.Acme.Orders);
    }

    /// <summary>Seeds a plan, optionally the account, and one due certificate with its site.</summary>
    /// <param name="accountExists">Whether the certificate's account has a row.</param>
    /// <returns>The identities the run needs.</returns>
    private async Task<World> SeedAsync(bool accountExists = true)
    {
        var accountId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        await using (var accounts = OpenAccounts())
        {
            accounts.Plans.Add(new Plan(
                PlanId, "PlanStarterName", diskQuotaMb: 5_120, maxSites: 7, maxDatabases: 2, maxSftpUsers: 3,
                maxCronEntries: 13, maxPhpWorkersPerPool: 11, maxFtpUsers: 17));
            if (accountExists)
            {
                accounts.Accounts.Add(new Account(
                    accountId, Username, Domain, PlanId, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            }

            await accounts.SaveChangesAsync();
        }

        await using (var ssl = OpenSsl())
        {
            ssl.Certificates.Add(new Maran.Modules.Ssl.Domain.Entities.Certificate(
                Guid.NewGuid(),
                accountId,
                siteId,
                Domain,
                CertificateSource.Acme,
                Now.AddDays(5),
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            await ssl.SaveChangesAsync();
        }

        return new World(accountId, siteId);
    }

    /// <summary>Builds the real renewal handler over real contexts and the recording seams.</summary>
    /// <param name="world">The seeded identities.</param>
    /// <returns>The handler and the seams it drives.</returns>
    private RenewalRun Run(World world)
    {
        var principal = new NoPrincipal();
        var accounts = OpenAccounts();
        var ssl = OpenSsl();
        var site = new SiteSnapshot(
            world.SiteId, world.AccountId, Domain, [], SiteBackend.Php, "8.3", string.Empty, HasCertificate: false);
        var sites = new KnownSiteDirectory(site);
        var acme = new RecordingAcmeClient();
        var agent = new RecordingSslAgent();
        var agentSites = new RecordingAgentSitesClient();
        var handler = new CertificateRenewalHandler(
            ssl,
            sites,
            new AccountDirectory(accounts, principal),
            acme,
            new CertificateInstaller(agent, sites),
            agentSites,
            new CertificateAuditJournal(new RecordingAuditWriter(), principal),
            new FixedInstantClock(),
            NullLogger<CertificateRenewalHandler>.Instance,
            new DiscardingTaskRecorder());

        return new RenewalRun(handler, acme, agent, agentSites);
    }

    /// <summary>Opens an Accounts context seen by nobody, as an unattended handler sees it.</summary>
    private AccountsDbContext OpenAccounts()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;

        return new AccountsDbContext(options, new NoPrincipal());
    }

    /// <summary>Opens an Ssl context seen by nobody, as an unattended handler sees it.</summary>
    private SslDbContext OpenSsl()
    {
        var options = new DbContextOptionsBuilder<SslDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;

        return new SslDbContext(options, new NoPrincipal(), new PlainEncryption());
    }

    /// <summary>The identities one seeded certificate hangs from.</summary>
    /// <param name="AccountId">The owning account.</param>
    /// <param name="SiteId">The site the certificate serves.</param>
    private sealed record World(Guid AccountId, Guid SiteId);

    /// <summary>The handler under test and the seams it drove.</summary>
    /// <param name="Handler">The real renewal handler.</param>
    /// <param name="Acme">The authority double.</param>
    /// <param name="Agent">The TLS agent double.</param>
    /// <param name="AgentSites">The sites agent double, which counts reloads.</param>
    private sealed record RenewalRun(
        CertificateRenewalHandler Handler,
        RecordingAcmeClient Acme,
        RecordingSslAgent Agent,
        RecordingAgentSitesClient AgentSites);

    /// <summary>An encryption double that stores what it is given; no test here reads ciphertext.</summary>
    private sealed class PlainEncryption : IEncryptionService
    {
        /// <inheritdoc />
        public string Encrypt(string plainText)
        {
            return plainText;
        }

        /// <inheritdoc />
        public string Decrypt(string cipherText)
        {
            return cipherText;
        }
    }

    /// <summary>A site window knowing one site, answering by id with no scope.</summary>
    /// <param name="site">The one site it knows.</param>
    private sealed class KnownSiteDirectory(SiteSnapshot site) : ISiteDirectory
    {
        /// <inheritdoc />
        public Task<SiteSnapshot?> FindByDomainAsync(string domain, CancellationToken cancellationToken)
        {
            return Task.FromResult<SiteSnapshot?>(domain == site.Domain ? site : null);
        }

        /// <inheritdoc />
        public Task<SiteSnapshot?> FindByIdUnscopedAsync(Guid siteId, CancellationToken cancellationToken)
        {
            return Task.FromResult<SiteSnapshot?>(siteId == site.Id ? site : null);
        }

        /// <inheritdoc />
        public Task<bool> AttachCertificateAsync(Guid siteId, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public Task<bool> DetachCertificateAsync(Guid siteId, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }

    /// <summary>An authority that issues fixed material and records every order.</summary>
    private sealed class RecordingAcmeClient : IAcmeClient
    {
        /// <summary>Every order placed, in order.</summary>
        public List<AcmeOrderRequest> Orders { get; } = [];

        /// <inheritdoc />
        public Task<Result<IssuedCertificate>> OrderAsync(
            AcmeOrderRequest request,
            CancellationToken cancellationToken)
        {
            Orders.Add(request);
            return Task.FromResult(Result<IssuedCertificate>.Ok(new IssuedCertificate(
                "-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----",
                "key-material",
                Now.AddDays(90))));
        }
    }

    /// <summary>A TLS agent that accepts every install and records the account and domain.</summary>
    private sealed class RecordingSslAgent : IAgentSslClient
    {
        /// <summary>The account and domain of every install, in order.</summary>
        public List<(string Account, string Domain)> Installs { get; } = [];

        /// <inheritdoc />
        public Task<Result<InstalledCertificateDto>> InstallCertificateAsync(
            string accountUsername,
            string domain,
            string certificatePem,
            string privateKeyPem,
            SiteDescriptor site,
            CancellationToken cancellationToken)
        {
            Installs.Add((accountUsername, domain));
            return Task.FromResult(Result<InstalledCertificateDto>.Ok(new InstalledCertificateDto(Now.AddDays(90))));
        }

        /// <inheritdoc />
        public Task<Result> RemoveCertificateAsync(
            string accountUsername,
            string domain,
            SiteDescriptor site,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Renewal removes no certificate.");
        }

        /// <inheritdoc />
        public Task<Result<InstalledCertificateDto>> InstallPanelCertificateAsync(
            string certificatePem,
            string privateKeyPem,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Renewal of a site certificate installs no panel certificate.");
        }

        /// <inheritdoc />
        public Task<Result> WritePanelChallengeAsync(
            string token,
            string keyAuthorization,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Renewal of a site certificate writes no panel challenge.");
        }

        /// <inheritdoc />
        public Task<Result> RemovePanelChallengeAsync(string token, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Renewal of a site certificate removes no panel challenge.");
        }
    }

    /// <summary>A task recorder that keeps nothing; the tasks module is not under test here.</summary>
    private sealed class DiscardingTaskRecorder : ITaskRecorder
    {
        /// <inheritdoc />
        public Task<Guid> BeginAsync(
            string kind,
            string subject,
            Guid? accountId,
            TaskAudience audience,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Guid.NewGuid());
        }

        /// <inheritdoc />
        public Task ReportAsync(Guid taskId, int percent, string line, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task CompleteAsync(Guid taskId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task FailAsync(Guid taskId, string errorCode, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
