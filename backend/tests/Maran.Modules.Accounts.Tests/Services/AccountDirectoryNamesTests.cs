using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="AccountDirectory.NamesOfAsync"/>: the batched name lookup
/// another module labels its rows with, scoped exactly as the single-account read is.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: <see cref="AccountDirectory.FindIdsByNameAsync"/>. It is an <c>ILIKE</c>, which
/// only PostgreSQL translates, so the InMemory provider cannot run it; <c>SiteListEndpointTests</c>
/// covers it against a real server through the sites list's owner search.
/// </remarks>
public sealed class AccountDirectoryNamesTests
{
    /// <summary>An administrator reads the name of every requested account in one call.</summary>
    [Fact]
    public async Task An_administrator_reads_the_name_of_every_requested_account_in_one_call()
    {
        await using var dbContext = CreateDbContext();
        var acme = await SeedAsync(dbContext, "acme");
        var globex = await SeedAsync(dbContext, "globex");
        await SeedAsync(dbContext, "initech");

        var names = await new AccountDirectory(dbContext, FakeCurrentUser.Admin())
            .NamesOfAsync([acme, globex, acme], CancellationToken.None);

        Assert.Equal(2, names.Count);
        Assert.Equal("acme", names[acme]);
        Assert.Equal("globex", names[globex]);
    }

    /// <summary>A customer reads only their own account's name, whatever ids are asked for.</summary>
    /// <remarks>
    /// The other tenant's id is real and would be answered for an administrator (the test above), so
    /// its absence here is the scope and not a missing row.
    /// </remarks>
    [Fact]
    public async Task A_customer_reads_only_their_own_accounts_name_whatever_ids_are_asked_for()
    {
        await using var dbContext = CreateDbContext();
        var own = await SeedAsync(dbContext, "acme");
        var stranger = await SeedAsync(dbContext, "globex");

        var names = await new AccountDirectory(dbContext, FakeCurrentUser.Customer(own))
            .NamesOfAsync([own, stranger], CancellationToken.None);

        var only = Assert.Single(names);
        Assert.Equal(own, only.Key);
        Assert.Equal("acme", only.Value);
    }

    /// <summary>A principal with neither admin nor an account reads no names.</summary>
    [Fact]
    public async Task A_principal_with_neither_admin_nor_an_account_reads_no_names()
    {
        await using var dbContext = CreateDbContext();
        var accountId = await SeedAsync(dbContext, "acme");

        var names = await new AccountDirectory(dbContext, new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: false))
            .NamesOfAsync([accountId], CancellationToken.None);

        Assert.Empty(names);
    }

    /// <summary>An unknown id is simply absent from the answer.</summary>
    [Fact]
    public async Task An_unknown_id_is_simply_absent_from_the_answer()
    {
        await using var dbContext = CreateDbContext();
        var acme = await SeedAsync(dbContext, "acme");

        var names = await new AccountDirectory(dbContext, FakeCurrentUser.Admin())
            .NamesOfAsync([acme, Guid.NewGuid()], CancellationToken.None);

        Assert.Equal(acme, Assert.Single(names).Key);
    }

    /// <summary>Builds a fresh, isolated in-memory context.</summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Seeds one account, returning its id. The name lookup reads no plan, so none is seeded.</summary>
    /// <param name="dbContext">The context to seed.</param>
    /// <param name="name">The account's name.</param>
    private static async Task<Guid> SeedAsync(AccountsDbContext dbContext, string name)
    {
        var account = new Account(
            Guid.NewGuid(),
            name,
            $"{name}.example.com",
            Guid.NewGuid(),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }
}
