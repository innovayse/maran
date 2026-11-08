using Maran.Modules.Dns.Commands.AddRecord;
using Maran.Modules.Dns.Commands.ChangeRecord;
using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Commands.DeleteRecord;
using Maran.Modules.Dns.Commands.DeleteZone;
using Maran.Modules.Dns.Commands.DisableDns;
using Maran.Modules.Dns.Commands.EnableDns;
using Maran.Modules.Dns.Commands.ReplaceRecords;
using Maran.Modules.Dns.Commands.SaveDnsSettings;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.IntegrationEvents.Handlers;
using Maran.Modules.Dns.Options;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Queries.GetDnsSettings;
using Maran.Modules.Dns.Queries.GetDnsStatus;
using Maran.Modules.Dns.Queries.GetZone;
using Maran.Modules.Dns.Queries.ListRecords;
using Maran.Modules.Dns.Queries.ListZones;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// One test's whole world: an in-memory database shared by name between principals, the recording agent and
/// the stand-ins for the other modules' windows, and the handlers wired exactly as the module wires them.
/// Two contexts over one database name are two callers over one database, which is how the isolation tests
/// put an administrator's rows in front of a customer.
/// </summary>
public sealed class DnsTestContext : IDisposable
{
    /// <summary>The module's database context, carrying the principal's tenant scope.</summary>
    public DnsDbContext Database { get; }

    /// <summary>The in-memory database's name, which a second context reuses to see the same rows.</summary>
    public string DatabaseName { get; }

    /// <summary>The agent stand-in.</summary>
    public RecordingDnsAgent Agent { get; } = new();

    /// <summary>Everything written to the audit journal.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>The accounts window.</summary>
    public StubAccountDirectory Accounts { get; } = new();

    /// <summary>The sites window.</summary>
    public StubSiteHostnameDirectory Sites { get; } = new();

    /// <summary>The reserved host names window.</summary>
    public StubReservedHostDirectory Reserved { get; } = new();

    /// <summary>The clock.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>The display-name lookup, answering each key with itself.</summary>
    public KeyEchoLocalizer Names { get; } = new();

    /// <summary>The gate counting an account's zones.</summary>
    public LocklessDnsZoneSlotGate SlotGate { get; }

    /// <summary>The zone lock every handler of this world takes, recording the names it was taken for.</summary>
    public ObservingDnsZoneLock ZoneLock { get; }

    /// <summary>The operator's settings: a small cap, so a full zone is cheap to reach.</summary>
    public IOptions<DnsOptions> Options { get; } = new OptionsWrapper<DnsOptions>(new DnsOptions { MaxRecordsPerZone = 3 });

    /// <summary>The handler that creates a zone.</summary>
    public CreateZoneCommandHandler CreateZone { get; }

    /// <summary>The handler that deletes a zone.</summary>
    public DeleteZoneCommandHandler DeleteZone { get; }

    /// <summary>The handler that adds a record.</summary>
    public AddRecordCommandHandler AddRecord { get; }

    /// <summary>The handler that changes a record.</summary>
    public ChangeRecordCommandHandler ChangeRecord { get; }

    /// <summary>The handler that deletes a record.</summary>
    public DeleteRecordCommandHandler DeleteRecord { get; }

    /// <summary>The handler that replaces a zone's records.</summary>
    public ReplaceRecordsCommandHandler ReplaceRecords { get; }

    /// <summary>The handler that saves the settings.</summary>
    public SaveDnsSettingsCommandHandler SaveSettings { get; }

    /// <summary>The handler that switches the name server on.</summary>
    public EnableDnsCommandHandler Enable { get; }

    /// <summary>The handler that switches the name server off.</summary>
    public DisableDnsCommandHandler Disable { get; }

    /// <summary>The handler that reads the settings.</summary>
    public GetDnsSettingsQueryHandler GetSettings { get; }

    /// <summary>The handler that reads the status.</summary>
    public GetDnsStatusQueryHandler GetStatus { get; }

    /// <summary>The handler that reads one zone.</summary>
    public GetZoneQueryHandler GetZone { get; }

    /// <summary>The handler that lists zones.</summary>
    public ListZonesQueryHandler ListZones { get; }

    /// <summary>The handler that lists a zone's records.</summary>
    public ListRecordsQueryHandler ListRecords { get; }

    /// <summary>The handler that removes an account's zones.</summary>
    public AccountDeletingHandler AccountDeleting { get; }

    /// <summary>The audit entries written so far.</summary>
    public IReadOnlyList<AuditEntry> Entries
    {
        get
        {
            return Audit.Entries;
        }
    }

    /// <summary>Creates a world for an administrator over a fresh database.</summary>
    public DnsTestContext()
        : this(new AdminCurrentUser(), Guid.NewGuid().ToString())
    {
    }

    /// <summary>Creates a world for one principal over the named database.</summary>
    /// <param name="principal">Who is acting.</param>
    /// <param name="databaseName">The in-memory database; reuse it to put two principals over one set of rows.</param>
    public DnsTestContext(ICurrentUser principal, string databaseName)
    {
        DatabaseName = databaseName;

        var options = new DbContextOptionsBuilder<DnsDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings =>
            {
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning);
            })
            .Options;

        Database = new DnsDbContext(options, principal);

        var journal = new DnsAuditJournal(Audit, principal);
        var zoneLock = new ObservingDnsZoneLock(Database);
        ZoneLock = zoneLock;
        var editor = new DnsZoneEditor(Database, Agent, zoneLock);
        SlotGate = new LocklessDnsZoneSlotGate(Database);

        CreateZone = new CreateZoneCommandHandler(
            Database,
            Accounts,
            Sites,
            Reserved,
            Agent,
            SlotGate,
            zoneLock,
            new ZoneNameDirectory(Database),
            journal,
            principal,
            Clock);
        DeleteZone = new DeleteZoneCommandHandler(Database, Agent, zoneLock, journal);
        AddRecord = new AddRecordCommandHandler(editor, Options, journal, Clock, Names);
        ChangeRecord = new ChangeRecordCommandHandler(editor, journal, Clock, Names);
        DeleteRecord = new DeleteRecordCommandHandler(editor, journal, Clock);
        ReplaceRecords = new ReplaceRecordsCommandHandler(editor, Options, journal, Clock);
        SaveSettings = new SaveDnsSettingsCommandHandler(Database, journal, Clock);
        Enable = new EnableDnsCommandHandler(Database, Agent, journal, Clock);
        Disable = new DisableDnsCommandHandler(Database, Agent, journal, Clock);
        GetSettings = new GetDnsSettingsQueryHandler(Database, Clock);
        GetStatus = new GetDnsStatusQueryHandler(Database, Agent);
        GetZone = new GetZoneQueryHandler(Database, Accounts);
        ListZones = new ListZonesQueryHandler(Database, Accounts);
        ListRecords = new ListRecordsQueryHandler(Database, Names);
        AccountDeleting = new AccountDeletingHandler(Database, Agent);
    }

    /// <summary>Stores settings that are complete and switched on, as an administrator would have.</summary>
    public async Task EnableDnsServerAsync()
    {
        var settings = new DnsSettings(Clock.UtcNow);
        settings.Save("ns1.panel.test", "ns2.panel.test", "hostmaster.panel.test", "203.0.113.5", string.Empty, 3600, Clock.UtcNow);
        settings.Enable(Clock.UtcNow);
        Database.DnsSettings.Add(settings);
        await Database.SaveChangesAsync();
    }

    /// <summary>Releases the database context.</summary>
    public void Dispose()
    {
        Database.Dispose();
    }
}
