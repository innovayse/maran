using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Npgsql;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// Assembles a handler and its doubles for one tenant, so each test states only what it is about.
/// </summary>
/// <remarks>
/// Every field is exposed so a test can assert on what the production path actually did — which
/// domain was ordered, which descriptor the agent was handed, which site flag was flipped — rather
/// than only on the value that came back. The fixture never performs any of those steps itself: the
/// tests drive the real handler, so a test cannot pass because its own setup did the work
/// (rules/testing.md).
/// </remarks>
public sealed class SslHandlerFixture : IDisposable
{
    /// <summary>The correlation id every request-driven handler in this fixture runs under.</summary>
    public const string Correlation = "corr-ssl";

    /// <summary>The instant every clock in this fixture reports.</summary>
    public static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The account every site in this fixture belongs to.</summary>
    public Guid AccountId { get; } = Guid.NewGuid();

    /// <summary>The system user name that account has on the host.</summary>
    public string Username { get; } = "acct";

    /// <summary>The site snapshots this fixture built, one per domain it was given.</summary>
    public SiteSnapshot[] Snapshots { get; }

    /// <summary>The tenant-scoped context under test.</summary>
    public SslDbContext DbContext { get; }

    /// <summary>The sites this caller owns, and the record of every certificate flag flipped.</summary>
    public StubSiteDirectory Sites { get; }

    /// <summary>The account directory the handlers read the system user name from.</summary>
    public StubAccountDirectory Accounts { get; }

    /// <summary>The certificate authority.</summary>
    public StubAcmeClient Acme { get; }

    /// <summary>The agent's TLS operations.</summary>
    public RecordingAgentSslClient Agent { get; }

    /// <summary>The agent's batch web-server reload.</summary>
    public RecordingAgentSitesClient AgentSites { get; }

    /// <summary>Everything the handlers journalled.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>The module's audit journal, over <see cref="Audit"/>.</summary>
    public CertificateAuditJournal Journal { get; }

    /// <summary>The shared install path the handlers use.</summary>
    public CertificateInstaller Installer { get; }

    /// <summary>The fixed clock every handler is given.</summary>
    public FakeClock Clock { get; } = new(Now);

    /// <summary>Everything the handlers recorded as panel tasks.</summary>
    public RecordingTaskRecorder Tasks { get; } = new();

    /// <summary>The correlation id the request-driven handlers put on the tasks they open.</summary>
    public StubCorrelationIdAccessor CorrelationIds { get; } = new(Correlation);

    /// <summary>Builds the fixture.</summary>
    /// <param name="domains">The domains of the sites the caller owns.</param>
    /// <param name="acmeFailure">A refusal for every order, or null to succeed.</param>
    /// <param name="agentFailure">A refusal for every agent TLS call, or null to succeed.</param>
    /// <param name="reloadFailure">A refusal for the batch reload, or null to succeed.</param>
    /// <param name="knowsAccount">Whether the account directory can answer for this account.</param>
    /// <param name="saveFailures">How many database writes should be refused, simulating a database failure.</param>
    /// <param name="saveFailureSqlState">The SQLSTATE those refusals carry; a unique violation by default.</param>
    public SslHandlerFixture(
        string[] domains,
        Error? acmeFailure = null,
        Error? agentFailure = null,
        Error? reloadFailure = null,
        bool knowsAccount = true,
        int saveFailures = 0,
        string saveFailureSqlState = PostgresErrorCodes.UniqueViolation)
    {
        var currentUser = FakeCurrentUser.Customer(AccountId);
        DbContext = SslTestContext.Create(
            currentUser,
            databaseName: null,
            saveFailures > 0 ? new UniqueViolationInterceptor(saveFailures, saveFailureSqlState) : null);
        Snapshots = domains.Select(domain =>
        {
            return new SiteSnapshot(
                Guid.NewGuid(),
                AccountId,
                domain,
                ["www." + domain],
                SiteBackend.Php,
                "8.3",
                string.Empty,
                HasCertificate: false);
        }).ToArray();
        Sites = new StubSiteDirectory(Snapshots);
        Accounts = knowsAccount
            ? new StubAccountDirectory(new AccountSnapshot(AccountId, Username, 10, 10, 10, 10, 10, 1_024))
            : new StubAccountDirectory();
        Acme = new StubAcmeClient(acmeFailure);
        Agent = new RecordingAgentSslClient(agentFailure);
        AgentSites = new RecordingAgentSitesClient(reloadFailure);
        Journal = new CertificateAuditJournal(Audit, currentUser);
        Installer = new CertificateInstaller(Agent, Sites);
    }

    /// <summary>Finds the snapshot this fixture built for one domain.</summary>
    /// <param name="domain">The domain to look up.</param>
    /// <returns>The snapshot.</returns>
    public SiteSnapshot SiteFor(string domain)
    {
        return Snapshots.Single(site =>
        {
            return string.Equals(site.Domain, domain, StringComparison.Ordinal);
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DbContext.Dispose();
    }
}
