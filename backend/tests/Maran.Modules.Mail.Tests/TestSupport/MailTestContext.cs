using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>
/// Everything a Mail handler test needs around the handler under test: a private in-memory database whose
/// tenant filter closes over a chosen principal, the recording agent, the stub windows onto the Accounts and
/// Sites modules, the journal and a few seeding helpers.
/// </summary>
/// <remarks>
/// Handlers are constructed by the tests that exercise them, from these parts; this context deliberately
/// knows no handler, so adding an operation edits no shared file. Two contexts given the same database name
/// share rows, which is how a cross-tenant test arranges customer B's resource and then asks as customer A.
/// </remarks>
public sealed class MailTestContext : IDisposable
{
    /// <summary>The Mail module's context, scoped to <see cref="Principal"/>.</summary>
    public MailDbContext Database { get; }

    /// <summary>The principal the tenant filter closes over.</summary>
    public ICurrentUser Principal { get; }

    /// <summary>The in-memory database's name.</summary>
    public string DatabaseName { get; }

    /// <summary>The recording agent.</summary>
    public RecordingMailAgent Agent { get; } = new();

    /// <summary>The recording audit writer.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>The accounts window.</summary>
    public StubAccountDirectory Accounts { get; } = new();

    /// <summary>The sites window.</summary>
    public StubSiteDirectory Sites { get; } = new();

    /// <summary>The fixed clock.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>The module's audit journal.</summary>
    public MailAuditJournal Journal { get; }

    /// <summary>The mailbox slot gate, without PostgreSQL.</summary>
    public LocklessMailboxSlotGate SlotGate { get; }

    /// <summary>The fake key generator.</summary>
    public StubDkimKeyGenerator Keys { get; } = new();

    /// <summary>The fake encryption.</summary>
    public ReversibleEncryption Encryption { get; } = new();

    /// <summary>The fake password hasher.</summary>
    public StubPasswordHasher Hasher { get; } = new();

    /// <summary>The save interceptor a test arms to make the next save fail.</summary>
    public ArmedSaveFailureInterceptor SaveFailure { get; } = new();

    /// <summary>Reads whether mail is enabled.</summary>
    public MailServerReader Server { get; }

    /// <summary>Hashes passwords the way the host verifies them.</summary>
    public MailPasswordHashing Hashing { get; }

    /// <summary>Reads whether accounts are suspended.</summary>
    public MailAccountStates AccountStates { get; }

    /// <summary>The audit entries written so far.</summary>
    public IReadOnlyList<AuditEntry> Entries
    {
        get
        {
            return Audit.Entries;
        }
    }

    /// <summary>Builds the context as an ADMINISTRATOR over its own private database.</summary>
    public MailTestContext()
        : this(new TestCurrentUser(), Guid.NewGuid().ToString())
    {
    }

    /// <summary>Builds the context as one principal over one named in-memory database.</summary>
    /// <param name="principal">Who the request is from; the tenant filter closes over this.</param>
    /// <param name="databaseName">Which in-memory database to attach to; the same name shares rows.</param>
    public MailTestContext(ICurrentUser principal, string databaseName)
    {
        Principal = principal;
        DatabaseName = databaseName;

        var options = new DbContextOptionsBuilder<MailDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(SaveFailure)
            .Options;

        Database = new MailDbContext(options, principal);
        Journal = new MailAuditJournal(Audit, principal);
        SlotGate = new LocklessMailboxSlotGate(Database);
        Server = new MailServerReader(Database);
        Hashing = new MailPasswordHashing(Hasher, Agent);
        AccountStates = new MailAccountStates(Accounts);
    }

    /// <summary>Stores an enabled settings row with the product defaults.</summary>
    /// <returns>The stored row.</returns>
    public async Task<MailServerSettings> SeedEnabledServerAsync()
    {
        var settings = new MailServerSettings(
            "mail.example.com",
            pop3Enabled: false,
            antivirusEnabled: false,
            allowExternalForwarding: false,
            MailDefaults.MessageSizeMib,
            MailDefaults.SpamGreylistScore,
            MailDefaults.SpamAddHeaderScore,
            MailDefaults.SpamRejectScore,
            Clock.UtcNow);
        Database.MailServerSettings.Add(settings);
        await Database.SaveChangesAsync();

        return settings;
    }

    /// <summary>Stores a domain with one active DKIM key.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="name">The domain name.</param>
    /// <returns>The stored domain.</returns>
    public async Task<MailDomain> SeedDomainAsync(Guid accountId, string name)
    {
        var domain = new MailDomain(Guid.NewGuid(), accountId, name, Guid.NewGuid(), Clock.UtcNow);
        domain.RotateDkim(
            new DkimKey(Guid.NewGuid(), domain.Id, "m20261107-1", "enc:key", "PUBLICKEY0", Clock.UtcNow),
            Clock.UtcNow);
        Database.MailDomains.Add(domain);
        await Database.SaveChangesAsync();

        return domain;
    }

    /// <summary>Stores a mailbox.</summary>
    /// <param name="domain">The domain it lives in.</param>
    /// <param name="localPart">The local part.</param>
    /// <param name="quotaMib">The quota, in MiB.</param>
    /// <returns>The stored mailbox.</returns>
    public async Task<Mailbox> SeedMailboxAsync(MailDomain domain, string localPart, int quotaMib = 1024)
    {
        var mailbox = new Mailbox(
            Guid.NewGuid(),
            domain.AccountId,
            domain.Id,
            localPart,
            StubPasswordHasher.Marker + "x",
            quotaMib,
            100,
            Clock.UtcNow);
        Database.Mailboxes.Add(mailbox);
        await Database.SaveChangesAsync();

        return mailbox;
    }

    /// <summary>Disposes the in-memory database.</summary>
    public void Dispose()
    {
        Database.Dispose();
    }
}
