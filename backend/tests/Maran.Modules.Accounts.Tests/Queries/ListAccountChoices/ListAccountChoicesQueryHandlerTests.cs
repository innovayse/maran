using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.ListAccountChoices;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Queries.ListAccountChoices;

/// <summary>
/// Behavioral contract of <see cref="ListAccountChoicesQueryHandler"/> over the EF Core InMemory
/// provider. UNOBSERVED HERE: the <c>ILIKE</c> search, which only PostgreSQL translates;
/// <c>AccountListEndpointTests</c> covers it.
/// </summary>
public sealed class ListAccountChoicesQueryHandlerTests
{
    /// <summary>Choices carry id and name, alphabetical, one page at a time with the full total.</summary>
    [Fact]
    public async Task Choices_carry_id_and_name_alphabetically_one_page_at_a_time_with_the_full_total()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new AccountsDbContext(options);
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var accounts = Enumerable.Range(0, 11).Reverse().Select(index =>
        {
            return new Account(Guid.NewGuid(), $"acct{index:D2}", $"acct{index:D2}.example.com", Guid.NewGuid(), created);
        }).ToList();
        dbContext.Accounts.AddRange(accounts);
        await dbContext.SaveChangesAsync();

        var result = await new ListAccountChoicesQueryHandler(dbContext)
            .HandleAsync(new ListAccountChoicesQuery(Page: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(11, result.Value.Total);
        var choice = Assert.Single(result.Value.Items);
        Assert.Equal("acct10", choice.Name);
        Assert.Equal(accounts.Single(account => { return account.Name == "acct10"; }).Id, choice.Id);
    }
}
