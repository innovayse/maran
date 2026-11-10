using Maran.Modules.ServerMigration.Commands.AddRemoteServer;
using Maran.Modules.ServerMigration.Commands.ConfirmRemoteServerHostKey;
using Maran.Modules.ServerMigration.Commands.DiscoverRemoteServer;
using Maran.Modules.ServerMigration.Commands.EditRemoteServer;
using Maran.Modules.ServerMigration.Commands.RemoveRemoteServer;
using Maran.Modules.ServerMigration.Commands.RescanRemoteServerHostKeys;
using Maran.Modules.ServerMigration.Commands.TestRemoteServer;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Queries.GetDiscoveredAccount;
using Maran.Modules.ServerMigration.Queries.GetRemoteServer;
using Maran.Modules.ServerMigration.Queries.ListDiscoveredAccounts;
using Maran.Modules.ServerMigration.Queries.ListRemoteServers;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>
/// One test's whole world: an in-memory database, the scripted agent, the recording audit writer and every handler of
/// the module wired as the module wires them. Reading a row back goes through <see cref="Reader"/>, a second context
/// over the same database, so a test sees what was SAVED and not what one context still tracks.
/// </summary>
public sealed class MigrationHandlerWorld : IDisposable
{
    /// <summary>The context the handlers write through.</summary>
    private readonly ServerMigrationDbContext _database;

    /// <summary>The in-memory database's name, which a second context reuses to see the same rows.</summary>
    public string DatabaseName { get; }

    /// <summary>A second context over the same database, for reading back what was saved.</summary>
    public ServerMigrationDbContext Reader { get; }

    /// <summary>The agent stand-in.</summary>
    public ScriptedTransferClient Agent { get; } = new();

    /// <summary>Everything written to the audit journal.</summary>
    public RecordedAuditEntries Audit { get; } = new();

    /// <summary>The clock.</summary>
    public HandlerClock Clock { get; } = new();

    /// <summary>The display-name lookup, answering each key with itself.</summary>
    public EchoDisplayNames Names { get; } = new();

    /// <summary>The handler that registers a server.</summary>
    public AddRemoteServerCommandHandler Add { get; }

    /// <summary>The handler that edits a server.</summary>
    public EditRemoteServerCommandHandler Edit { get; }

    /// <summary>The handler that rescans a server's host keys.</summary>
    public RescanRemoteServerHostKeysCommandHandler Rescan { get; }

    /// <summary>The handler that pins a host key.</summary>
    public ConfirmRemoteServerHostKeyCommandHandler Confirm { get; }

    /// <summary>The handler that tests a server.</summary>
    public TestRemoteServerCommandHandler Test { get; }

    /// <summary>The handler that removes a server.</summary>
    public RemoveRemoteServerCommandHandler Remove { get; }

    /// <summary>The handler that runs a discovery.</summary>
    public DiscoverRemoteServerCommandHandler Discover { get; }

    /// <summary>The handler that reads one server.</summary>
    public GetRemoteServerQueryHandler Get { get; }

    /// <summary>The handler that lists servers.</summary>
    public ListRemoteServersQueryHandler List { get; }

    /// <summary>The handler that lists a server's discovered accounts.</summary>
    public ListDiscoveredAccountsQueryHandler ListAccounts { get; }

    /// <summary>The handler that reads one discovered account.</summary>
    public GetDiscoveredAccountQueryHandler GetAccount { get; }

    /// <summary>The audit entries written so far.</summary>
    public IReadOnlyList<AuditEntry> Entries
    {
        get { return Audit.Entries; }
    }

    /// <summary>Creates a world over a fresh database.</summary>
    public MigrationHandlerWorld()
    {
        DatabaseName = Guid.NewGuid().ToString();
        _database = InMemoryMigrationDatabase.Open(DatabaseName);
        Reader = InMemoryMigrationDatabase.Open(DatabaseName);

        var journal = new ServerMigrationAuditJournal(Audit, new AdministratorUser());

        Add = new AddRemoteServerCommandHandler(_database, Agent, journal, Clock, Names);
        Edit = new EditRemoteServerCommandHandler(_database, journal, Names);
        Rescan = new RescanRemoteServerHostKeysCommandHandler(_database, Agent, journal, Names);
        Confirm = new ConfirmRemoteServerHostKeyCommandHandler(_database, journal, Clock, Names);
        Test = new TestRemoteServerCommandHandler(_database, Agent, journal, Clock, Names);
        Remove = new RemoveRemoteServerCommandHandler(_database, journal);
        Discover = new DiscoverRemoteServerCommandHandler(_database, Agent, journal, Clock, Names);
        Get = new GetRemoteServerQueryHandler(_database, Names);
        List = new ListRemoteServersQueryHandler(_database, Names);
        ListAccounts = new ListDiscoveredAccountsQueryHandler(_database);
        GetAccount = new GetDiscoveredAccountQueryHandler(_database, Names);
    }

    /// <summary>Stores a server directly, as if an earlier request had registered it.</summary>
    /// <param name="server">The server, in whatever state the test needs.</param>
    /// <returns>A task that completes when the server is saved.</returns>
    public async Task SeedAsync(RemoteServer server)
    {
        using var seeding = InMemoryMigrationDatabase.Open(DatabaseName);
        seeding.RemoteServers.Add(server);
        await seeding.SaveChangesAsync();
    }

    /// <summary>Stores a discovered account directly, as if an earlier discovery had found it.</summary>
    /// <param name="account">The account, with its sites and databases.</param>
    /// <returns>A task that completes when the account is saved.</returns>
    public async Task SeedAsync(DiscoveredAccount account)
    {
        using var seeding = InMemoryMigrationDatabase.Open(DatabaseName);
        seeding.DiscoveredAccounts.Add(account);
        await seeding.SaveChangesAsync();
    }

    /// <summary>Stores a discovery warning directly, as if an earlier discovery had raised it.</summary>
    /// <param name="warning">The warning.</param>
    /// <returns>A task that completes when the warning is saved.</returns>
    public async Task SeedAsync(DiscoveryWarning warning)
    {
        using var seeding = InMemoryMigrationDatabase.Open(DatabaseName);
        seeding.DiscoveryWarnings.Add(warning);
        await seeding.SaveChangesAsync();
    }

    /// <summary>Releases both contexts.</summary>
    public void Dispose()
    {
        _database.Dispose();
        Reader.Dispose();
    }
}
