using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// Behavioural contract of the handler that suspends an account's memberships and revokes the
/// sessions speaking for it (issue #326): the suspension belongs to the account, so a person who
/// holds two accounts keeps the other one.
/// </summary>
public sealed class AccountSuspendingHandlerTests : IDisposable
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private static readonly Guid OtherAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static AccountSuspending Suspending(Guid? accountId = null)
    {
        return new AccountSuspending(accountId ?? AccountId, "owner-one");
    }

    private AccountSuspendingHandler NewHandler()
    {
        return new AccountSuspendingHandler(_context, NewSessions());
    }

    private SessionService NewSessions()
    {
        return new SessionService(_context, _clock, new OptionsWrapper<JwtOptions>(new JwtOptions { RefreshTokenDays = 14 }));
    }

    /// <summary>Stores a claimed customer login that owns <paramref name="accountIds"/>, oldest first.</summary>
    /// <param name="accountIds">The accounts the login owns.</param>
    private async Task<User> SeedActiveOwnerAsync(params Guid[] accountIds)
    {
        var user = new User(Guid.NewGuid(), "owner-one", "owner@example.com", "hash", UserRole.Customer, Now);
        user.Activate("hash");
        _context.Users.Add(user);
        foreach (var accountId in accountIds)
        {
            _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), user.Id, accountId, Now));
        }

        await _context.SaveChangesAsync();
        return user;
    }

    private async Task<MembershipState> StateOfAsync(Guid userId, Guid accountId)
    {
        return (await _context.AccountMemberships.SingleAsync(
            candidate => candidate.UserId == userId && candidate.AccountId == accountId)).State;
    }

    /// <summary>
    /// Suspension revokes the live sessions of an already-signed-in owner, not merely the next
    /// sign-in.
    /// </summary>
    /// <remarks>
    /// This is the property the type's own remarks name as the half of a suspension that actually
    /// matters: suspending the membership alone leaves a session issued before the suspension
    /// authenticating for the rest of its own lifetime. Removing the revocation call from the handler
    /// must turn this test red — asserting only that the MEMBERSHIP is suspended would not catch that
    /// regression, because the membership and the session are two separate rows.
    /// </remarks>
    [Fact]
    public async Task Suspension_revokes_the_live_sessions_of_an_already_signed_in_owner()
    {
        var user = await SeedActiveOwnerAsync(AccountId);
        var issued = await NewSessions().IssueAsync(user.Id, "203.0.113.7", "browser", AccountId, CancellationToken.None);

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        var session = await _context.Sessions.SingleAsync(candidate => candidate.Id == issued.SessionId);
        Assert.False(session.IsActive(Now));
        Assert.NotNull(session.Revocation);
        Assert.Equal(SessionRevocationReason.AccountSuspended, session.Revocation?.Reason);
    }

    /// <summary>Suspending an account suspends the owner's membership and leaves the login itself alone.</summary>
    [Fact]
    public async Task Suspending_an_account_suspends_the_owners_membership_and_leaves_the_login_alone()
    {
        var user = await SeedActiveOwnerAsync(AccountId);

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        Assert.Equal(MembershipState.Suspended, await StateOfAsync(user.Id, AccountId));
        Assert.Equal(UserLifecycle.Active, (await _context.Users.SingleAsync()).Lifecycle);
    }

    /// <summary>Suspending an account suspends every added member's membership as well as the owner's.</summary>
    /// <remarks>
    /// Before suspension was per membership only the owner's login was found, so a member the owner
    /// had added kept signing in to a suspended account.
    /// </remarks>
    [Fact]
    public async Task Suspending_an_account_suspends_every_added_members_membership_too()
    {
        var owner = await SeedActiveOwnerAsync(AccountId);
        var member = new User(Guid.NewGuid(), "member-one", "member@example.com", "hash", UserRole.Customer, Now);
        _context.Users.Add(member);
        _context.AccountMemberships.Add(
            AccountMembership.Admit(Guid.NewGuid(), member.Id, AccountId, Maran.Sdk.Contracts.AccountFeature.None, Now));
        await _context.SaveChangesAsync();

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        Assert.Equal(MembershipState.Suspended, await StateOfAsync(owner.Id, AccountId));
        Assert.Equal(MembershipState.Suspended, await StateOfAsync(member.Id, AccountId));
    }

    /// <summary>
    /// Suspending the SECOND account of a person who holds two suspends that one and leaves the first
    /// open: the original defect found the login through one account column and could not.
    /// </summary>
    [Fact]
    public async Task Suspending_the_second_account_of_a_person_with_two_leaves_the_first_open()
    {
        var user = await SeedActiveOwnerAsync(AccountId, OtherAccountId);

        await NewHandler().HandleAsync(Suspending(OtherAccountId), CancellationToken.None);

        Assert.Equal(MembershipState.Suspended, await StateOfAsync(user.Id, OtherAccountId));
        Assert.Equal(MembershipState.Active, await StateOfAsync(user.Id, AccountId));
    }

    /// <summary>
    /// Suspending one account ends the sessions speaking for it and keeps the person signed in to
    /// their other account.
    /// </summary>
    [Fact]
    public async Task Suspending_one_account_ends_only_the_sessions_speaking_for_it()
    {
        var user = await SeedActiveOwnerAsync(AccountId, OtherAccountId);
        var sessions = NewSessions();
        var speakingForFirst = await sessions.IssueAsync(user.Id, "203.0.113.7", "a", AccountId, CancellationToken.None);
        var speakingForSecond = await sessions.IssueAsync(user.Id, "203.0.113.8", "b", OtherAccountId, CancellationToken.None);

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        var ended = await _context.Sessions.SingleAsync(candidate => candidate.Id == speakingForFirst.SessionId);
        var kept = await _context.Sessions.SingleAsync(candidate => candidate.Id == speakingForSecond.SessionId);
        Assert.False(ended.IsActive(Now));
        Assert.True(kept.IsActive(Now));
    }

    /// <summary>Suspension leaves an invited login invited, so the unused invitation survives.</summary>
    /// <remarks>
    /// An owner who never accepted their invitation has no password and no session to revoke; the
    /// suspension is recorded on the membership, so the login keeps its invitation and accepting it
    /// during the suspension still produces no way in.
    /// </remarks>
    [Fact]
    public async Task Suspension_leaves_an_invited_login_invited_and_suspends_its_membership()
    {
        var user = User.Invite(Guid.NewGuid(), "owner-one", "owner@example.com", Now);
        _context.Users.Add(user);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), user.Id, AccountId, Now));
        await _context.SaveChangesAsync();

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        Assert.Equal(UserLifecycle.Invited, (await _context.Users.SingleAsync()).Lifecycle);
        Assert.Equal(MembershipState.Suspended, await StateOfAsync(user.Id, AccountId));
    }

    /// <summary>Suspending an account restates the owner's legacy pair as suspended, as the previous release reads it.</summary>
    /// <remarks>
    /// A rollback to the previous release reads <c>State</c> / <c>IsAccountSuspended</c>, so an owner
    /// left at Active there could sign in to a suspended account again.
    /// </remarks>
    [Fact]
    public async Task Suspending_an_account_restates_the_owners_legacy_pair_as_suspended()
    {
        await SeedActiveOwnerAsync(AccountId);

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        var owner = await _context.Users.SingleAsync();
        Assert.Equal((UserState.Suspended, true), (owner.LegacyState, owner.LegacyAccountSuspended));
    }

    /// <summary>An invited owner of the suspended account is restated as Invited with the flag set.</summary>
    [Fact]
    public async Task Suspending_an_account_restates_an_invited_owners_legacy_pair_as_invited_and_suspended()
    {
        var user = User.Invite(Guid.NewGuid(), "owner-one", "owner@example.com", Now);
        _context.Users.Add(user);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), user.Id, AccountId, Now));
        await _context.SaveChangesAsync();

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        var owner = await _context.Users.SingleAsync();
        Assert.Equal((UserState.Invited, true), (owner.LegacyState, owner.LegacyAccountSuspended));
    }

    /// <summary>An added member's login is not restated: it owns nothing the previous release could suspend.</summary>
    [Fact]
    public async Task Suspending_an_account_leaves_an_added_members_legacy_pair_alone()
    {
        await SeedActiveOwnerAsync(AccountId);
        var member = new User(Guid.NewGuid(), "member-one", "member@example.com", "hash", UserRole.Customer, Now);
        member.Activate("hash");
        _context.Users.Add(member);
        _context.AccountMemberships.Add(
            AccountMembership.Admit(Guid.NewGuid(), member.Id, AccountId, Maran.Sdk.Contracts.AccountFeature.None, Now));
        await _context.SaveChangesAsync();

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        var stored = await _context.Users.SingleAsync(candidate => candidate.Id == member.Id);
        Assert.Equal((UserState.Active, false), (stored.LegacyState, stored.LegacyAccountSuspended));
    }

    /// <summary>Another account's memberships are not touched by a suspension.</summary>
    [Fact]
    public async Task Another_accounts_memberships_are_not_touched()
    {
        var user = await SeedActiveOwnerAsync(OtherAccountId);

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        Assert.Equal(MembershipState.Active, await StateOfAsync(user.Id, OtherAccountId));
    }

    /// <summary>An account with no membership at all is left with nothing to do.</summary>
    [Fact]
    public async Task An_account_with_no_membership_at_all_is_left_with_nothing_to_do()
    {
        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        Assert.Empty(await _context.AccountMemberships.ToListAsync());
        Assert.Empty(await _context.Sessions.ToListAsync());
    }
}
