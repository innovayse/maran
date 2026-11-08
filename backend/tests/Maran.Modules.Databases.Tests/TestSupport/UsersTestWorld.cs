using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Services;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Databases.Tests.TestSupport;

/// <summary>
/// Two accounts on one shared in-memory store, seen through the customer who owns the first, with the
/// doubles every database-user handler test needs wired the way the Host wires them.
/// </summary>
/// <remarks>
/// The owner (<c>alice</c>) has the database <c>shop</c> with its dedicated user <c>shopuser</c> holding
/// every privilege, and an additional user <c>reader</c> holding nothing. The stranger (<c>bob</c>) has a
/// database and a dedicated user named the same. The seed is written through an administrator's context
/// so it does no tenant separating of its own: the filters under test are the only thing that can.
/// </remarks>
public sealed class UsersTestWorld
{
    /// <summary>The instant every seeded row and every new row is stamped with.</summary>
    public static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The account the signed-in customer owns.</summary>
    public static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>The other tenant.</summary>
    public static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>The shared store's name, for opening a second context onto it.</summary>
    public string Store { get; }

    /// <summary>The module's context, seen as the signed-in caller.</summary>
    public DatabasesDbContext Context { get; }

    /// <summary>The agent double every call is recorded on.</summary>
    public RecordingAgentDbClient Agent { get; } = new();

    /// <summary>The journal every entry lands in.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>The journal the handlers write through.</summary>
    public DatabaseAuditJournal Journal { get; }

    /// <summary>The user limit gate, without the real one's advisory lock.</summary>
    public LocklessDatabaseUserLimitGate LimitGate { get; }

    /// <summary>The directory the handlers resolve accounts through.</summary>
    public StubAccountDirectory Accounts { get; }

    /// <summary>The owner's database <c>alice_shop</c>.</summary>
    public Guid OwnDatabaseId { get; private set; }

    /// <summary>The owner's dedicated user <c>alice_shopuser</c>.</summary>
    public Guid DedicatedUserId { get; private set; }

    /// <summary>The owner's additional user <c>alice_reader</c>, holding nothing.</summary>
    public Guid ReaderUserId { get; private set; }

    /// <summary>The other tenant's database <c>bob_shop</c>.</summary>
    public Guid StrangerDatabaseId { get; private set; }

    /// <summary>The other tenant's dedicated user <c>bob_shopuser</c>.</summary>
    public Guid StrangerUserId { get; private set; }

    /// <summary>Wires one world over a fresh store; use <see cref="CreateAsync"/>.</summary>
    /// <param name="store">The name of the shared in-memory store.</param>
    /// <param name="saveFailure">What the customer context's saves throw, when the test kills them.</param>
    /// <param name="caller">Who the handlers run as.</param>
    private UsersTestWorld(string store, Exception? saveFailure, FakeCurrentUser caller)
    {
        Store = store;
        Context = DatabasesTestContext.Create(caller, store, saveFailure);
        LimitGate = new LocklessDatabaseUserLimitGate(Context);
        Journal = new DatabaseAuditJournal(Audit, caller);
        Accounts = new StubAccountDirectory(
            new AccountSnapshot(OwnerAccountId, "alice", 5, 5, 5, 5, 5, 1_024, AccountState.Active),
            new AccountSnapshot(StrangerAccountId, "bob", 5, 5, 5, 5, 5, 1_024, AccountState.Active));
    }

    /// <summary>Builds and seeds a world.</summary>
    /// <param name="saveFailure">What the customer context's saves throw, when the test kills them.</param>
    /// <param name="asAdministrator">Run the handlers as an administrator, whom no tenant filter narrows.</param>
    /// <returns>The seeded world.</returns>
    public static async Task<UsersTestWorld> CreateAsync(Exception? saveFailure = null, bool asAdministrator = false)
    {
        var caller = asAdministrator ? FakeCurrentUser.Admin() : FakeCurrentUser.Customer(OwnerAccountId);
        var world = new UsersTestWorld(Guid.NewGuid().ToString(), null, caller);
        await world.SeedAsync();

        // A second world over the same store whose saves fail: the seed above must not trip the failure.
        if (saveFailure is null)
        {
            return world;
        }

        var failing = new UsersTestWorld(world.Store, saveFailure, caller)
        {
            OwnDatabaseId = world.OwnDatabaseId,
            DedicatedUserId = world.DedicatedUserId,
            ReaderUserId = world.ReaderUserId,
            StrangerDatabaseId = world.StrangerDatabaseId,
            StrangerUserId = world.StrangerUserId,
        };

        return failing;
    }

    /// <summary>Reads every user row of the store, bypassing every tenant filter.</summary>
    /// <returns>All rows with their grants.</returns>
    public async Task<List<DatabaseUser>> AllUsersAsync()
    {
        await using var admin = DatabasesTestContext.Create(FakeCurrentUser.Admin(), Store);

        return await admin.DatabaseUsers.AsNoTracking().Include(user => user.Grants).ToListAsync();
    }

    /// <summary>A logger the handlers write to and nothing reads.</summary>
    /// <typeparam name="T">The handler type the logger is for.</typeparam>
    /// <returns>A logger that discards.</returns>
    public static NullLogger<T> Log<T>()
    {
        return NullLogger<T>.Instance;
    }

    /// <summary>Writes the seed through an administrator's context.</summary>
    private async Task SeedAsync()
    {
        await using var admin = DatabasesTestContext.Create(FakeCurrentUser.Admin(), Store);

        var own = DatabasesTestContext.Row(OwnerAccountId, "alice", "shop", "shopuser");
        var stranger = DatabasesTestContext.Row(StrangerAccountId, "bob", "shop", "shopuser");
        admin.Databases.AddRange(own, stranger);

        var dedicated = new DatabaseUser(Guid.NewGuid(), OwnerAccountId, "shopuser", "alice_shopuser", Now);
        dedicated.ReplacePrivileges(own.Id, DatabasePrivilegeSet.Select | DatabasePrivilegeSet.Insert, Guid.NewGuid(), Now);
        var reader = new DatabaseUser(Guid.NewGuid(), OwnerAccountId, "reader", "alice_reader", Now);
        var strangerUser = new DatabaseUser(Guid.NewGuid(), StrangerAccountId, "shopuser", "bob_shopuser", Now);
        strangerUser.ReplacePrivileges(stranger.Id, DatabasePrivilegeSet.Select, Guid.NewGuid(), Now);
        admin.DatabaseUsers.AddRange(dedicated, reader, strangerUser);
        await admin.SaveChangesAsync();

        OwnDatabaseId = own.Id;
        DedicatedUserId = dedicated.Id;
        ReaderUserId = reader.Id;
        StrangerDatabaseId = stranger.Id;
        StrangerUserId = strangerUser.Id;
    }
}
