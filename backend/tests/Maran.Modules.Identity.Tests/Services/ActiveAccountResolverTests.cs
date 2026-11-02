using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;

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

        Assert.Equal(oldest, resolved);
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

        Assert.Equal(newer, resolved);
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

        Assert.Equal(owned, resolved);
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

        Assert.Null(resolved);
    }

    /// <summary>An administrator owns no account and acts for none.</summary>
    [Fact]
    public async Task An_administrator_acts_for_no_account()
    {
        var admin = new User(Guid.NewGuid(), "admin", "admin@example.com", "hash", UserRole.Admin, Now);
        _context.Users.Add(admin);
        await OwnAsync(admin, Now);

        var resolved = await new ActiveAccountResolver(_context).ResolveAsync(admin, null, CancellationToken.None);

        Assert.Null(resolved);
    }
}
