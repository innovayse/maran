using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// The setting every sub-user test starts from: one account with an owner signed in, a clock, a
/// recording journal and a bus, over a fresh in-memory database.
/// </summary>
public sealed class SubUserWorld : IDisposable
{
    /// <summary>The instant the clock starts at.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The account the owner owns.</summary>
    public static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>The database, shared by everything the test builds.</summary>
    public IdentityDbContext Context { get; } = IdentityTestContext.Create();

    /// <summary>What the journal wrote.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>What was published.</summary>
    public RecordingMessageBus Bus { get; } = new();

    /// <summary>The accounts the test knows.</summary>
    public StubAccountDirectory Accounts { get; } = new();

    /// <summary>The panel's clock.</summary>
    public FakeClock Clock { get; } = new(Now);

    /// <summary>The owner's login.</summary>
    public User Owner { get; }

    /// <summary>The owner signed in, acting for <see cref="AccountId"/>.</summary>
    public StubCustomerUser Caller { get; }

    /// <summary>The journal over <see cref="Audit"/>, naming <see cref="Caller"/>.</summary>
    public IdentityAuditJournal Journal { get; }

    /// <summary>The stand-in for the invitation lock, counting commits and rollbacks.</summary>
    public EmulatedTeamLock TeamLock { get; }

    /// <summary>Creates the world with the owner's login and ownership already stored.</summary>
    public SubUserWorld()
    {
        Accounts.Add(AccountId, "acme", maxFtpUsers: 1);
        Owner = new User(Guid.NewGuid(), "acme", "owner@example.com", "hash", Maran.Modules.Identity.Domain.Enums.UserRole.Customer, Now);
        Context.Users.Add(Owner);
        Context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), Owner.Id, AccountId, Now));
        Context.SaveChanges();
        Caller = new StubCustomerUser(Owner.Id, Owner.Username, AccountId);
        Journal = new IdentityAuditJournal(Audit, Caller);
        TeamLock = new EmulatedTeamLock(Context);
    }

    /// <summary>The guard over <paramref name="caller"/>.</summary>
    /// <param name="caller">Who is asking; defaults to the owner.</param>
    /// <returns>The guard.</returns>
    public SubUserOwnerGuard Guard(ICurrentUser? caller = null)
    {
        return new SubUserOwnerGuard(Context, caller ?? Caller);
    }

    /// <summary>Stores an accepted sub-user of <paramref name="accountId"/> with <paramref name="grants"/>.</summary>
    /// <param name="username">The login name.</param>
    /// <param name="grants">The features the member holds.</param>
    /// <param name="accountId">The account they belong to; defaults to the owner's.</param>
    /// <returns>The member's login.</returns>
    public async Task<User> SeedMemberAsync(string username, AccountFeature grants, Guid? accountId = null)
    {
        var user = User.Invite(Guid.NewGuid(), username, username + "@example.com", Now);
        user.Activate("hash");
        Context.Users.Add(user);
        Context.AccountMemberships.Add(
            AccountMembership.Admit(Guid.NewGuid(), user.Id, accountId ?? AccountId, grants, Now));
        await Context.SaveChangesAsync();
        return user;
    }

    /// <summary>Releases the database.</summary>
    public void Dispose()
    {
        Context.Dispose();
    }
}
