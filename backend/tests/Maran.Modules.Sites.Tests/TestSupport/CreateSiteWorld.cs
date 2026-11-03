using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// Everything one <see cref="CreateSiteCommandHandler"/> run needs, assembled once so each test states only
/// its own variation. Shared by the CreateSite handler test classes.
/// </summary>
public sealed class CreateSiteWorld
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory database shared by every context this world hands out.</summary>
    public string Database { get; }

    /// <summary>The context the handler is given.</summary>
    public SitesDbContext DbContext { get; }

    /// <summary>The agent double.</summary>
    public RecordingAgentSitesClient Agent { get; }

    /// <summary>The PHP runtime double.</summary>
    public RecordingAgentPhpClient Php { get; }

    /// <summary>The account directory double.</summary>
    public StubAccountDirectory Accounts { get; }

    /// <summary>The audit journal double.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>The slot gate the handler claims through, without the real one's advisory lock.</summary>
    /// <remarks>
    /// A test sets <see cref="LocklessSiteSlotGate.RefuseNextClaim"/> to reach the handler's late
    /// refusal, which in production is a concurrent commit and is not reachable on this provider.
    /// </remarks>
    public LocklessSiteSlotGate SlotGate { get; }

    /// <summary>The logger the handler reports a failed or withheld compensation to.</summary>
    public RecordingLogger<SiteVhostCompensator> Logger { get; } = new();

    /// <summary>The principal the context and the handler share.</summary>
    public FakeCurrentUser CurrentUser { get; }

    /// <summary>Assembles the world.</summary>
    /// <param name="accountId">The account the caller owns.</param>
    /// <param name="maxSites">The plan's site allowance.</param>
    /// <param name="installedPhp">Versions the host reports as installed.</param>
    /// <param name="agentFailure">When set, the agent refuses every site operation.</param>
    /// <param name="phpFailure">When set, the agent cannot list PHP versions.</param>
    /// <param name="createFailure">When set, the agent refuses only the creation.</param>
    /// <param name="deleteFailure">When set, the agent refuses only the delete.</param>
    /// <param name="asAdministrator">When true the caller is an administrator, whom no tenant filter narrows.</param>
    public CreateSiteWorld(
        Guid accountId,
        int maxSites = 5,
        string[]? installedPhp = null,
        Error? agentFailure = null,
        Error? phpFailure = null,
        Error? createFailure = null,
        Error? deleteFailure = null,
        bool asAdministrator = false)
    {
        Database = Guid.NewGuid().ToString();
        CurrentUser = asAdministrator ? FakeCurrentUser.Admin() : FakeCurrentUser.Customer(accountId);
        DbContext = SitesTestContext.Create(CurrentUser, Database);
        SlotGate = new LocklessSiteSlotGate(DbContext);
        Agent = agentFailure is null
            ? new RecordingAgentSitesClient { CreateFailure = createFailure, DeleteFailure = deleteFailure }
            : new RecordingAgentSitesClient(agentFailure) { CreateFailure = createFailure, DeleteFailure = deleteFailure };
        Php = phpFailure is null
            ? new RecordingAgentPhpClient(installedPhp ?? ["8.3"])
            : new RecordingAgentPhpClient(phpFailure);
        Accounts = new StubAccountDirectory(new AccountSnapshot(
            accountId,
            "acme",
            maxSites,
            MaxDatabases: 2,
            MaxSftpUsers: 3,
            MaxCronEntries: 7,
            MaxPhpWorkersPerPool: 10,
            DiskQuotaMb: 1_024, AccountState.Active));
    }

    /// <summary>Arms the slot gate so the next claim loses to a rival that commits first, as the hostname key refuses it.</summary>
    /// <param name="rivalDomain">The primary domain the rival site holds.</param>
    /// <param name="rivalAliases">The aliases the rival site holds.</param>
    /// <param name="beforeThrow">Runs after the rival commits and before the refusal is thrown.</param>
    public void LoseTo(string rivalDomain, string[] rivalAliases, Action? beforeThrow = null)
    {
        SlotGate.BeforeNextClaim = async () =>
        {
            await CommitRivalAsync(rivalDomain, rivalAliases);
            beforeThrow?.Invoke();
        };
        SlotGate.ThrowOnNextClaim = GateFailures.UniqueViolation();
    }

    /// <summary>Arms the slot gate so a rival with other names commits during the host work and the claim then succeeds.</summary>
    /// <param name="rivalDomain">The primary domain the rival site holds.</param>
    /// <param name="rivalAliases">The aliases the rival site holds.</param>
    public void CommitRivalBeforeClaim(string rivalDomain, string[] rivalAliases)
    {
        SlotGate.BeforeNextClaim = () =>
        {
            return CommitRivalAsync(rivalDomain, rivalAliases);
        };
    }

    /// <summary>Reads, across every account, the primary domain of every committed site in this world.</summary>
    /// <returns>The domains, ordinally sorted.</returns>
    public async Task<IReadOnlyList<string>> AllDomainsAsync()
    {
        await using var admin = SitesTestContext.Create(FakeCurrentUser.Admin(), Database);
        var domains = await admin.Sites.Select(site => site.Domain).ToListAsync();

        return domains.OrderBy(
            domain =>
            {
                return domain;
            },
            StringComparer.Ordinal).ToList();
    }

    /// <summary>Builds the command under test.</summary>
    /// <param name="accountId">The account the site is created for.</param>
    /// <param name="domain">The primary domain requested.</param>
    /// <param name="aliases">The additional hostnames requested.</param>
    public static CreateSiteCommand Command(
        Guid accountId,
        string domain = "example.com",
        string[]? aliases = null)
    {
        return new CreateSiteCommand(
            accountId,
            domain,
            SiteBackendType.Php,
            aliases ?? ["www.example.com"],
            "8.3",
            string.Empty,
            "198.51.100.7",
            "tests");
    }

    /// <summary>Builds the handler under test.</summary>
    /// <returns>A handler wired to this world's doubles.</returns>
    public CreateSiteCommandHandler Handler()
    {
        var claims = new SiteHostnameClaims(DbContext);

        return new CreateSiteCommandHandler(
            DbContext,
            Accounts,
            Agent,
            Php,
            claims,
            new SiteRecorder(SlotGate, new SiteVhostCompensator(claims, Agent, Logger)),
            new SiteAuditJournal(Audit, CurrentUser),
            new FakeClock(Now),
            SitesTestContext.SiteNames());
    }

    /// <summary>Commits a site of ANOTHER account holding the given names, as a concurrent creation would.</summary>
    /// <param name="domain">The rival's primary domain.</param>
    /// <param name="aliases">The rival's aliases.</param>
    private async Task CommitRivalAsync(string domain, string[] aliases)
    {
        await using var admin = SitesTestContext.Create(FakeCurrentUser.Admin(), Database);
        admin.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), domain, "8.3", aliases));
        await admin.SaveChangesAsync();
    }
}
