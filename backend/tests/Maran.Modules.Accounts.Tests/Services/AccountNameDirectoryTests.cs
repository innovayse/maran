using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="AccountNameDirectory"/>: the account selector's name lookup, which
/// applies no tenant scope of its own and reads past the context's fail-closed query filter explicitly.
/// </summary>
/// <remarks>
/// Every test opens its reading context for the very caller it asks as, over a database seeded as an
/// administrator, because the bypass is only observable against a context whose filter hides the rows:
/// read through an administrator's context the same answer would come back with or without it.
/// </remarks>
public sealed class AccountNameDirectoryTests
{
    /// <summary>A customer is named every account of their memberships, not only the one they act for.</summary>
    /// <remarks>
    /// This is the selector's whole purpose: the context shows the customer only the account it acts
    /// for, so the other account's name is read only because the lookup bypasses the filter.
    /// </remarks>
    [Fact]
    public async Task A_customer_is_named_every_account_of_their_memberships_not_only_the_active_one()
    {
        var database = Guid.NewGuid().ToString();
        var (active, other, _) = await SeedAsync(database);
        var customer = FakeCurrentUser.Customer(active);
        await using var dbContext = AccountsTestContext.CreateOn(database, customer);

        var names = await new AccountNameDirectory(dbContext)
            .NamesOfAsync([active, other], CancellationToken.None);

        Assert.Equal(2, names.Count);
        Assert.Equal("acme", names[active]);
        Assert.Equal("globex", names[other]);
    }

    /// <summary>A customer login acting for no account is named the accounts it holds memberships of.</summary>
    [Fact]
    public async Task A_customer_acting_for_no_account_is_named_the_accounts_it_holds()
    {
        var database = Guid.NewGuid().ToString();
        var (first, second, _) = await SeedAsync(database);
        await using var dbContext = AccountsTestContext.CreateOn(database, FakeCurrentUser.AccountlessCustomer());

        var names = await new AccountNameDirectory(dbContext)
            .NamesOfAsync([first, second], CancellationToken.None);

        Assert.Equal(2, names.Count);
    }

    /// <summary>An anonymous caller is named the accounts it is asked about.</summary>
    /// <remarks>
    /// The control reads the same database through the anonymous context and finds no account, so the
    /// names above come from the bypass and not from an open filter.
    /// </remarks>
    [Fact]
    public async Task An_anonymous_caller_is_named_the_accounts_it_is_asked_about()
    {
        var database = Guid.NewGuid().ToString();
        var (first, second, _) = await SeedAsync(database);
        await using var dbContext = AccountsTestContext.CreateOn(database, FakeCurrentUser.Anonymous());
        Assert.Empty(await dbContext.Accounts.ToListAsync());

        var names = await new AccountNameDirectory(dbContext)
            .NamesOfAsync([first, second], CancellationToken.None);

        Assert.Equal("acme", names[first]);
        Assert.Equal("globex", names[second]);
    }

    /// <summary>Only the requested accounts are named, whoever asks, and an unknown id is absent.</summary>
    [Fact]
    public async Task Only_the_requested_accounts_are_named_and_an_unknown_id_is_absent()
    {
        var database = Guid.NewGuid().ToString();
        var (first, _, _) = await SeedAsync(database);
        ICurrentUser[] callers =
        [
            FakeCurrentUser.Anonymous(),
            FakeCurrentUser.Customer(first),
            FakeCurrentUser.Admin(),
        ];

        foreach (var caller in callers)
        {
            await using var dbContext = AccountsTestContext.CreateOn(database, caller);

            var names = await new AccountNameDirectory(dbContext)
                .NamesOfAsync([first, Guid.NewGuid(), first], CancellationToken.None);

            Assert.Equal(first, Assert.Single(names).Key);
        }
    }

    /// <summary>Asking for no ids names nothing.</summary>
    [Fact]
    public async Task Asking_for_no_ids_names_nothing()
    {
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database);
        await using var dbContext = AccountsTestContext.CreateOn(database, FakeCurrentUser.Anonymous());

        var names = await new AccountNameDirectory(dbContext).NamesOfAsync([], CancellationToken.None);

        Assert.Empty(names);
    }

    /// <summary>Seeds three accounts as an administrator, returning the first three ids.</summary>
    /// <param name="database">The in-memory database to seed.</param>
    /// <returns>The ids of "acme", "globex" and "initech".</returns>
    private static async Task<(Guid Acme, Guid Globex, Guid Initech)> SeedAsync(string database)
    {
        await using var seed = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin());
        var acme = NewAccount("acme");
        var globex = NewAccount("globex");
        var initech = NewAccount("initech");
        seed.Accounts.AddRange(acme, globex, initech);
        await seed.SaveChangesAsync();
        return (acme.Id, globex.Id, initech.Id);
    }

    /// <summary>Builds an account on a plan nothing stores: the name lookup reads no plan.</summary>
    /// <param name="name">The account's name.</param>
    /// <returns>The account.</returns>
    private static Account NewAccount(string name)
    {
        return new Account(
            Guid.NewGuid(),
            name,
            $"{name}.example.com",
            Guid.NewGuid(),
            ResellerTestData.CreatedAt);
    }
}
