using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ListMyAccounts;
using Maran.Modules.Identity.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Queries.ListMyAccounts;

/// <summary>Behavioural contract of the list of accounts a login may switch between.</summary>
public sealed class ListMyAccountsQueryHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The names of the two accounts the person owns, in listing order.</summary>
    private static readonly string[] BlogAndShop = { "blog", "shop" };

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly StubAccountNameDirectory _names = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private async Task<Guid> OwnAsync(Guid userId, string name)
    {
        var accountId = Guid.NewGuid();
        _names.Add(accountId, name);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), userId, accountId, Now));
        await _context.SaveChangesAsync();

        return accountId;
    }

    private ListMyAccountsQueryHandler NewHandler()
    {
        return new ListMyAccountsQueryHandler(_context, _names);
    }

    /// <summary>The list names every account the caller holds, ordered by name, and marks the active one.</summary>
    [Fact]
    public async Task The_list_names_every_account_ordered_by_name_and_marks_the_active_one()
    {
        var userId = Guid.NewGuid();
        var shop = await OwnAsync(userId, "shop");
        var blog = await OwnAsync(userId, "blog");

        var result = await NewHandler().HandleAsync(new ListMyAccountsQuery(userId, shop), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BlogAndShop, result.Value.Select(row =>
        {
            return row.Name;
        }));
        Assert.False(result.Value.Single(row =>
        {
            return row.AccountId == blog;
        }).IsCurrent);
        Assert.True(result.Value.Single(row =>
        {
            return row.AccountId == shop;
        }).IsCurrent);
    }

    /// <summary>Another login's accounts are neither listed nor even asked about.</summary>
    [Fact]
    public async Task Another_logins_accounts_are_neither_listed_nor_asked_about()
    {
        var userId = Guid.NewGuid();
        var mine = await OwnAsync(userId, "mine");
        var theirs = await OwnAsync(Guid.NewGuid(), "theirs");

        var result = await NewHandler().HandleAsync(new ListMyAccountsQuery(userId, mine), CancellationToken.None);

        Assert.Equal(new[] { mine }, result.Value.Select(row =>
        {
            return row.AccountId;
        }));
        Assert.DoesNotContain(theirs, _names.Asked);
    }

    /// <summary>An account whose name cannot be read is left out rather than listed unlabelled.</summary>
    [Fact]
    public async Task An_account_that_no_longer_exists_is_left_out()
    {
        var userId = Guid.NewGuid();
        var mine = await OwnAsync(userId, "mine");
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), userId, Guid.NewGuid(), Now));
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(new ListMyAccountsQuery(userId, mine), CancellationToken.None);

        Assert.Equal(new[] { mine }, result.Value.Select(row =>
        {
            return row.AccountId;
        }));
    }

    /// <summary>A suspended account is not a choice the selector offers, and is not even asked about.</summary>
    [Fact]
    public async Task A_suspended_account_is_left_out_and_not_asked_about()
    {
        var userId = Guid.NewGuid();
        var mine = await OwnAsync(userId, "mine");
        var suspended = await OwnAsync(userId, "suspended");
        (await _context.AccountMemberships.SingleAsync(candidate => candidate.AccountId == suspended)).Suspend();
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(new ListMyAccountsQuery(userId, mine), CancellationToken.None);

        Assert.Equal(new[] { mine }, result.Value.Select(row =>
        {
            return row.AccountId;
        }));
        Assert.DoesNotContain(suspended, _names.Asked);
    }
}
