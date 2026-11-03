using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// Behavioural contract of the handler that lets the logins of a reactivated account act for it again
/// (issue #326): resuming acts on that account's memberships and on nothing else.
/// </summary>
public sealed class AccountResumingHandlerTests : IDisposable
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private static readonly Guid OtherAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Stores a claimed login owning each of <paramref name="accountIds"/>, every membership suspended.</summary>
    /// <param name="accountIds">The accounts the login owns.</param>
    private async Task<User> SeedSuspendedOwnerAsync(params Guid[] accountIds)
    {
        var user = new User(Guid.NewGuid(), "owner-one", "owner@example.com", "hash", UserRole.Customer, Now);
        user.Activate("hash");
        _context.Users.Add(user);
        foreach (var accountId in accountIds)
        {
            var membership = AccountMembership.Own(Guid.NewGuid(), user.Id, accountId, Now);
            membership.Suspend();
            _context.AccountMemberships.Add(membership);
        }

        user.SyncLegacyColumns(MembershipState.Suspended);

        await _context.SaveChangesAsync();
        return user;
    }

    private async Task<MembershipState> StateOfAsync(Guid userId, Guid accountId)
    {
        return (await _context.AccountMemberships.SingleAsync(
            candidate => candidate.UserId == userId && candidate.AccountId == accountId)).State;
    }

    /// <summary>Resuming an account makes its suspended membership active again.</summary>
    [Fact]
    public async Task Resuming_an_account_makes_its_suspended_membership_active_again()
    {
        var user = await SeedSuspendedOwnerAsync(AccountId);

        await new AccountResumingHandler(_context).HandleAsync(
            new AccountResuming(AccountId, "owner-one"), CancellationToken.None);

        Assert.Equal(MembershipState.Active, await StateOfAsync(user.Id, AccountId));
    }

    /// <summary>Resuming one account does not resume another account the same person holds.</summary>
    [Fact]
    public async Task Resuming_one_account_does_not_resume_another_the_same_person_holds()
    {
        var user = await SeedSuspendedOwnerAsync(AccountId, OtherAccountId);

        await new AccountResumingHandler(_context).HandleAsync(
            new AccountResuming(OtherAccountId, "owner-two"), CancellationToken.None);

        Assert.Equal(MembershipState.Active, await StateOfAsync(user.Id, OtherAccountId));
        Assert.Equal(MembershipState.Suspended, await StateOfAsync(user.Id, AccountId));
    }

    /// <summary>Resuming an account clears the owner's legacy suspension pair that the suspension set.</summary>
    [Fact]
    public async Task Resuming_an_account_clears_the_owners_legacy_suspension_pair()
    {
        await SeedSuspendedOwnerAsync(AccountId);
        Assert.Equal((UserState.Suspended, true), await LegacyPairAsync());

        await new AccountResumingHandler(_context).HandleAsync(
            new AccountResuming(AccountId, "owner-one"), CancellationToken.None);

        Assert.Equal((UserState.Active, false), await LegacyPairAsync());
    }

    /// <summary>The legacy pair stays suspended while the owner still owns another suspended account.</summary>
    [Fact]
    public async Task The_legacy_pair_stays_suspended_while_another_owned_account_is_still_suspended()
    {
        await SeedSuspendedOwnerAsync(AccountId, OtherAccountId);

        await new AccountResumingHandler(_context).HandleAsync(
            new AccountResuming(OtherAccountId, "owner-two"), CancellationToken.None);

        Assert.Equal((UserState.Suspended, true), await LegacyPairAsync());
    }

    /// <summary>Reads the stored owner's legacy state and suspension flag, as the previous release would.</summary>
    private async Task<(UserState State, bool Suspended)> LegacyPairAsync()
    {
        var owner = await _context.Users.SingleAsync();
        return (owner.LegacyState, owner.LegacyAccountSuspended);
    }

    /// <summary>An account with no membership is left with nothing to do.</summary>
    [Fact]
    public async Task An_account_with_no_membership_is_left_with_nothing_to_do()
    {
        await new AccountResumingHandler(_context).HandleAsync(
            new AccountResuming(AccountId, "owner-one"), CancellationToken.None);

        Assert.Empty(await _context.AccountMemberships.ToListAsync());
    }
}
