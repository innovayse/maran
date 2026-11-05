using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sftp.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>
/// The data and doubles every key test needs, wired the way the Host wires them: two accounts, three
/// logins each holding one key, and a customer of the first account as the caller.
/// </summary>
/// <remarks>
/// <c>alice</c>'s account owns the logins <c>deploy</c> (one Ed25519 key) and <c>backup</c> (one P-256
/// key); <c>bob</c>'s account owns <c>nightly</c> (one RSA key). The caller is a customer of
/// <c>alice</c>, so <c>nightly</c> and its key can be reached only by the tenant filter failing.
/// </remarks>
public sealed class SftpKeyTestWorld
{
    /// <summary>The account the caller is a customer of.</summary>
    public static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>The other tenant.</summary>
    public static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>The instant the seeded keys were registered.</summary>
    public static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory store every context in this world reads.</summary>
    public string Store { get; }

    /// <summary>The caller's login <c>deploy</c>, holding one key.</summary>
    public Guid OwnLoginId { get; private set; }

    /// <summary>The key of <see cref="OwnLoginId"/>.</summary>
    public Guid OwnKeyId { get; private set; }

    /// <summary>The caller's second login <c>backup</c>, holding one key.</summary>
    public Guid SiblingLoginId { get; private set; }

    /// <summary>The key of <see cref="SiblingLoginId"/>.</summary>
    public Guid SiblingKeyId { get; private set; }

    /// <summary>The other tenant's login <c>nightly</c>, holding one key.</summary>
    public Guid StrangerLoginId { get; private set; }

    /// <summary>The key of <see cref="StrangerLoginId"/>.</summary>
    public Guid StrangerKeyId { get; private set; }

    /// <summary>The caller.</summary>
    public ICurrentUser CurrentUser { get; } = FakeCurrentUser.Customer(OwnerAccountId);

    /// <summary>The agent double every call is recorded on.</summary>
    public RecordingAgentSftpClient Agent { get; } = new();

    /// <summary>The journal every entry lands in.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>The key lock double.</summary>
    public StubSftpUserKeyLock KeyLock { get; } = new();

    /// <summary>The account directory, knowing both accounts.</summary>
    public StubAccountDirectory Accounts { get; } = new(
        new AccountSnapshot(OwnerAccountId, "alice", 5, 5, 5, 5, 5, 1_024, AccountState.Active),
        new AccountSnapshot(StrangerAccountId, "bob", 5, 5, 5, 5, 5, 1_024, AccountState.Active));

    /// <summary>The module's audit journal, as the caller.</summary>
    public SftpAuditJournal Journal { get; }

    /// <summary>Creates an empty world; use <see cref="CreateAsync"/>.</summary>
    private SftpKeyTestWorld()
    {
        Store = Guid.NewGuid().ToString();
        Journal = new SftpAuditJournal(Audit, CurrentUser);
    }

    /// <summary>Builds a world and seeds its logins and keys.</summary>
    /// <returns>The seeded world.</returns>
    public static async Task<SftpKeyTestWorld> CreateAsync()
    {
        var world = new SftpKeyTestWorld();

        // Written through an administrator's context, so the seed does no tenant separating of its own.
        using var seed = SftpTestContext.Create(FakeCurrentUser.Admin(), world.Store);
        var own = SftpTestContext.Row(OwnerAccountId, "alice", "deploy");
        var sibling = SftpTestContext.Row(OwnerAccountId, "alice", "backup");
        var stranger = SftpTestContext.Row(StrangerAccountId, "bob", "nightly");
        world.OwnKeyId = AddSeedKey(own, "existing", SftpTestKeys.Ed25519);
        world.SiblingKeyId = AddSeedKey(sibling, "sibling", SftpTestKeys.P256);
        world.StrangerKeyId = AddSeedKey(stranger, "stranger", SftpTestKeys.Rsa2048);
        seed.SftpUsers.AddRange(own, sibling, stranger);
        await seed.SaveChangesAsync();

        world.OwnLoginId = own.Id;
        world.SiblingLoginId = sibling.Id;
        world.StrangerLoginId = stranger.Id;

        return world;
    }

    /// <summary>Opens a context over the world's store, seen as the caller.</summary>
    /// <param name="saveFailure">When given, the exception the next save throws.</param>
    /// <returns>The caller's tenant-filtered context.</returns>
    public SftpDbContext CallerContext(Exception? saveFailure = null)
    {
        return SftpTestContext.Create(CurrentUser, Store, saveFailure);
    }

    /// <summary>Opens a context over the world's store that no tenant filter narrows.</summary>
    /// <returns>An administrator's context, for asserting what was stored.</returns>
    public SftpDbContext AdminContext()
    {
        return SftpTestContext.Create(FakeCurrentUser.Admin(), Store);
    }

    /// <summary>Adds a key to a login that is not yet saved.</summary>
    /// <param name="login">The login.</param>
    /// <param name="label">The key's label.</param>
    /// <param name="line">The key line.</param>
    /// <returns>The new key's id.</returns>
    private static Guid AddSeedKey(SftpUser login, string label, string line)
    {
        var id = Guid.NewGuid();
        login.AddKey(id, label, SftpPublicKey.Parse(line).Value, Epoch);

        return id;
    }
}
