using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.GetAccount;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Queries.GetAccount;
/// <summary>Behavioural contract of get account query handler.</summary>

public sealed class GetAccountQueryHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly AccountsDbContext _context = CreateDbContext();

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private async Task<Account> SeedAsync()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Now);
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    /// <summary>Reading an existing account returns its details.</summary>
    [Fact]
    public async Task Reading_an_existing_account_returns_its_details()
    {
        var account = await SeedAsync();

        var result = await new GetAccountQueryHandler(_context).HandleAsync(
            new GetAccountQuery(account.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("acme", result.Value.Name);
        Assert.Equal("acme.example.com", result.Value.PrimaryDomain);
        Assert.Equal(AccountStatus.Active, result.Value.Status);
    }

    /// <summary>Reading an account that does not exist answers not found.</summary>
    [Fact]
    public async Task Reading_an_account_that_does_not_exist_answers_not_found()
    {
        await SeedAsync();

        var result = await new GetAccountQueryHandler(_context).HandleAsync(
            new GetAccountQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
    }
}
