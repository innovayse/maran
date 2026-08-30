using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.ListAccounts;
using Microsoft.EntityFrameworkCore;
using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Tests.Queries.ListAccounts;

/// <summary>
/// Behavioral contract of <see cref="ListAccountsQueryHandler"/>, run against a real
/// <see cref="AccountsDbContext"/> backed by the EF Core InMemory provider, each test getting its
/// own uniquely-named database (rules/testing.md "Determinism").
/// </summary>
public sealed class ListAccountsQueryHandlerTests
{
    /// <summary>Builds a fresh, isolated in-memory <see cref="AccountsDbContext"/>.</summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    [Fact]
    public async Task Empty_store_returns_an_empty_list()
    {
        await using var dbContext = CreateDbContext();
        var handler = new ListAccountsQueryHandler(dbContext);

        var result = await handler.Handle(new ListAccountsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Returns_every_account_the_store_holds_mapped_to_account_dto()
    {
        await using var dbContext = CreateDbContext();
        var planId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", planId, createdAt);
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
        var handler = new ListAccountsQueryHandler(dbContext);

        var result = await handler.Handle(new ListAccountsQuery(), CancellationToken.None);

        var dto = Assert.Single(result.Value);
        Assert.Equal(account.Id, dto.Id);
        Assert.Equal("acme", dto.Name);
        Assert.Equal("acme.example.com", dto.PrimaryDomain);
        Assert.Equal(planId, dto.PlanId);
        Assert.Equal(AccountStatus.Active, dto.Status);
        Assert.Equal(createdAt, dto.CreatedAt);
    }

    [Fact]
    public async Task Returns_accounts_ordered_by_creation_time_ascending()
    {
        await using var dbContext = CreateDbContext();
        var earliest = new Account(Guid.NewGuid(), "first", "first.example.com", Guid.NewGuid(),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var middle = new Account(Guid.NewGuid(), "second", "second.example.com", Guid.NewGuid(),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var latest = new Account(Guid.NewGuid(), "third", "third.example.com", Guid.NewGuid(),
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        // Inserted out of chronological order so the assertion cannot pass by insertion-order accident.
        dbContext.Accounts.AddRange(latest, earliest, middle);
        await dbContext.SaveChangesAsync();
        var handler = new ListAccountsQueryHandler(dbContext);

        var result = await handler.Handle(new ListAccountsQuery(), CancellationToken.None);

        Assert.Equal(["first", "second", "third"], result.Value.Select(dto =>
        {
            return dto.Name;
        }));
    }
}
