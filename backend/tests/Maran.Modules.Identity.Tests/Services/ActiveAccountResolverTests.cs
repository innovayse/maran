using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>Behavioural contract of the choice of the account a sign-in speaks for.</summary>
public sealed class ActiveAccountResolverTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static User NewCustomer()
    {
        return new User(Guid.NewGuid(), "customer", "customer@example.com", "hash", UserRole.Customer, Now);
    }

    private async Task<Guid> OwnAsync(User user, DateTimeOffset at)
    {
        var accountId = Guid.NewGuid();
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), user.Id, accountId, at));
        await _context.SaveChangesAsync();

        return accountId;
    }

    /// <summary>A new sign-in starts inside the oldest account the login owns.</summary>
    [Fact]
    public async Task A_new_sign_in_starts_inside_the_oldest_owned_account()
    {
        var user = NewCustomer();
        _context.Users.Add(user);
        var oldest = await OwnAsync(user, Now.AddDays(-2));
        await OwnAsync(user, Now);

        var resolved = await new ActiveAccountResolver(_context).ResolveAsync(user, null, CancellationToken.None);

        Assert.Equal(oldest, resolved.Value);
    }

    /// <summary>A remembered choice survives while the login still holds a membership of that account.</summary>
    [Fact]
    public async Task A_remembered_choice_survives_while_the_membership_stands()
    {
        var user = NewCustomer();
        _context.Users.Add(user);
        await OwnAsync(user, Now.AddDays(-2));
        var newer = await OwnAsync(user, Now);

        var resolved = await new ActiveAccountResolver(_context).ResolveAsync(user, newer, CancellationToken.None);

        Assert.Equal(newer, resolved.Value);
    }

    /// <summary>A remembered account the login holds no membership of is replaced, never trusted.</summary>
    [Fact]
    public async Task A_remembered_account_without_a_membership_is_replaced_by_the_default()
    {
        var user = NewCustomer();
        _context.Users.Add(user);
        var owned = await OwnAsync(user, Now);

        var resolved = await new ActiveAccountResolver(_context)
            .ResolveAsync(user, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(owned, resolved.Value);
    }

    /// <summary>Another login's membership of an account never makes it available.</summary>
    [Fact]
    public async Task Another_logins_membership_is_never_available()
    {
        var user = NewCustomer();
        var stranger = new User(Guid.NewGuid(), "stranger", "stranger@example.com", "hash", UserRole.Customer, Now);
        _context.Users.AddRange(user, stranger);
        var theirs = await OwnAsync(stranger, Now);

        var resolved = await new ActiveAccountResolver(_context).ResolveAsync(user, theirs, CancellationToken.None);

        Assert.Null(resolved.Value);
    }

    /// <summary>An administrator owns no account and acts for none.</summary>
    [Fact]
    public async Task An_administrator_acts_for_no_account()
    {
        var admin = new User(Guid.NewGuid(), "admin", "admin@example.com", "hash", UserRole.Admin, Now);
        _context.Users.Add(admin);
        await OwnAsync(admin, Now);

        var resolved = await new ActiveAccountResolver(_context).ResolveAsync(admin, null, CancellationToken.None);

        Assert.Null(resolved.Value);
    }

    /// <summary>Suspends the membership of <paramref name="accountId"/> held by <paramref name="user"/>.</summary>
    /// <param name="user">The login whose membership is suspended.</param>
    /// <param name="accountId">The account the membership is for.</param>
    private async Task SuspendAsync(User user, Guid accountId)
    {
        var membership = await _context.AccountMemberships.SingleAsync(
            candidate => candidate.UserId == user.Id && candidate.AccountId == accountId);
        membership.Suspend();
        await _context.SaveChangesAsync();
    }

    /// <summary>A suspended account is never chosen: the sign-in starts inside the next one.</summary>
    [Fact]
    public async Task A_suspended_account_is_never_chosen_by_default()
    {
        var user = NewCustomer();
        _context.Users.Add(user);
        var oldest = await OwnAsync(user, Now.AddDays(-2));
        var newer = await OwnAsync(user, Now);
        await SuspendAsync(user, oldest);

        var resolved = await new ActiveAccountResolver(_context).ResolveAsync(user, null, CancellationToken.None);

        Assert.Equal(newer, resolved.Value);
    }

    /// <summary>A remembered account that has since been suspended is replaced at the next refresh.</summary>
    [Fact]
    public async Task A_remembered_account_that_was_suspended_is_replaced()
    {
        var user = NewCustomer();
        _context.Users.Add(user);
        var first = await OwnAsync(user, Now.AddDays(-2));
        var second = await OwnAsync(user, Now);
        await SuspendAsync(user, second);

        var resolved = await new ActiveAccountResolver(_context).ResolveAsync(user, second, CancellationToken.None);

        Assert.Equal(first, resolved.Value);
    }

    /// <summary>A login whose every account is suspended is refused, not signed in to nothing.</summary>
    [Fact]
    public async Task A_login_whose_every_account_is_suspended_is_refused()
    {
        var user = NewCustomer();
        _context.Users.Add(user);
        var only = await OwnAsync(user, Now);
        await SuspendAsync(user, only);

        var resolved = await new ActiveAccountResolver(_context).ResolveAsync(user, only, CancellationToken.None);

        Assert.False(resolved.IsSuccess);
        Assert.Equal("AccountMembershipSuspended", resolved.Error!.Code);
        Assert.Equal(ErrorType.Conflict, resolved.Error.Type);
    }

    /// <summary>A customer with no membership at all is not a suspended one.</summary>
    [Fact]
    public async Task A_customer_with_no_membership_at_all_resolves_to_no_account()
    {
        var user = NewCustomer();
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        var resolver = new ActiveAccountResolver(_context);

        var resolved = await resolver.ResolveAsync(user, null, CancellationToken.None);

        Assert.True(resolved.IsSuccess);
        Assert.Null(resolved.Value);
        Assert.False(await resolver.IsSuspendedEverywhereAsync(user, CancellationToken.None));
    }

    /// <summary>Holding one active account among suspended ones is not being suspended everywhere.</summary>
    [Fact]
    public async Task One_active_account_among_suspended_ones_is_not_suspended_everywhere()
    {
        var user = NewCustomer();
        _context.Users.Add(user);
        var suspended = await OwnAsync(user, Now.AddDays(-2));
        await OwnAsync(user, Now);
        await SuspendAsync(user, suspended);

        Assert.False(await new ActiveAccountResolver(_context).IsSuspendedEverywhereAsync(user, CancellationToken.None));
    }

    /// <summary>A reseller acts for no account: it resolves to none, and it is never "suspended everywhere".</summary>
    /// <remarks>
    /// Even a stray membership row on a reseller login is not read: a reseller reaches an account only
    /// through a view-as, never by acting for it.
    /// </remarks>
    [Fact]
    public async Task A_reseller_resolves_to_no_account_and_is_never_suspended_everywhere()
    {
        var reseller = User.InviteReseller(Guid.NewGuid(), "northwind", "n@example.com", Guid.NewGuid(), Now);
        reseller.Activate("hash");
        _context.Users.Add(reseller);
        await OwnAsync(reseller, Now);
        var resolver = new ActiveAccountResolver(_context);

        var resolved = await resolver.ResolveAsync(reseller, null, CancellationToken.None);

        Assert.True(resolved.IsSuccess);
        Assert.Null(resolved.Value);
        Assert.False(await resolver.IsSuspendedEverywhereAsync(reseller, CancellationToken.None));
    }
}
