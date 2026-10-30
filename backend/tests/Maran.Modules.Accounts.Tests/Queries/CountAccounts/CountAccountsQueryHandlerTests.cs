using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.CountAccounts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Queries.CountAccounts;

/// <summary>Behavioral contract of <see cref="CountAccountsQueryHandler"/>.</summary>
public sealed class CountAccountsQueryHandlerTests
{
    /// <summary>The count is every account, not the size of one list page.</summary>
    [Fact]
    public async Task The_count_is_every_account_not_the_size_of_one_list_page()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new AccountsDbContext(options);
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        dbContext.Accounts.AddRange(Enumerable.Range(0, 30).Select(index =>
        {
            return new Account(Guid.NewGuid(), $"acct{index:D2}", $"acct{index:D2}.example.com", Guid.NewGuid(), created);
        }));
        await dbContext.SaveChangesAsync();

        var result = await new CountAccountsQueryHandler(dbContext)
            .HandleAsync(new CountAccountsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Value);
    }
}
