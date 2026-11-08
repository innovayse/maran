using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>What <see cref="ResellerDirectory"/>, the window other modules read through, answers per caller.</summary>
public sealed class ResellerDirectoryTests
{
    /// <summary>An administrator reads any reseller, with its state.</summary>
    [Fact]
    public async Task An_administrator_reads_any_reseller_with_its_state()
    {
        var active = ResellerTestData.Reseller("active");
        var suspended = ResellerTestData.Reseller("suspended");
        suspended.Suspend();
        await using var dbContext = await SeedAsync(FakeCurrentUser.Admin(), active, suspended);
        var directory = new ResellerDirectory(dbContext, FakeCurrentUser.Admin());

        var first = await directory.FindAsync(active.Id, CancellationToken.None);
        var second = await directory.FindAsync(suspended.Id, CancellationToken.None);

        Assert.Equal(
            new ResellerSnapshot(active.Id, "active", "active@example.com", ResellerState.Active, active.Language),
            first);
        Assert.Equal(
            new ResellerSnapshot(
                suspended.Id,
                "suspended",
                "suspended@example.com",
                ResellerState.Suspended,
                suspended.Language),
            second);
    }

    /// <summary>A reseller reads itself and not another reseller.</summary>
    [Fact]
    public async Task A_reseller_reads_itself_and_not_another_reseller()
    {
        var own = ResellerTestData.Reseller("mine");
        var other = ResellerTestData.Reseller("theirs");
        var currentUser = FakeCurrentUser.Reseller(own.Id);
        await using var dbContext = await SeedAsync(currentUser, own, other);
        var directory = new ResellerDirectory(dbContext, currentUser);

        Assert.NotNull(await directory.FindAsync(own.Id, CancellationToken.None));
        Assert.Null(await directory.FindAsync(other.Id, CancellationToken.None));
    }

    /// <summary>A reseller asking for another's id is answered exactly as for an id that does not exist.</summary>
    [Fact]
    public async Task Another_resellers_id_is_answered_like_an_id_that_does_not_exist()
    {
        var own = ResellerTestData.Reseller("mine");
        var currentUser = FakeCurrentUser.Reseller(own.Id);
        await using var dbContext = await SeedAsync(currentUser, own);
        var directory = new ResellerDirectory(dbContext, currentUser);

        Assert.Null(await directory.FindAsync(Guid.NewGuid(), CancellationToken.None));
    }

    /// <summary>A customer, an accountless customer and an anonymous caller read no reseller.</summary>
    [Fact]
    public async Task A_caller_that_is_neither_administrator_nor_reseller_reads_none()
    {
        var reseller = ResellerTestData.Reseller();
        await using var dbContext = await SeedAsync(FakeCurrentUser.Admin(), reseller);

        var callers = new[]
        {
            FakeCurrentUser.Customer(Guid.NewGuid()),
            FakeCurrentUser.AccountlessCustomer(),
            FakeCurrentUser.Anonymous(),
        };
        foreach (var caller in callers)
        {
            var directory = new ResellerDirectory(dbContext, caller);

            Assert.Null(await directory.FindAsync(reseller.Id, CancellationToken.None));
        }
    }

    /// <summary>The same callers read no reseller when the context is built for each of them.</summary>
    /// <remarks>
    /// What is observed here is the directory's own scope check: it refuses these callers before it reads
    /// the context, so the answer would be the same with the context's query filter removed. The filter
    /// itself is proved in <c>AccountsDbContextFailClosedTests</c>. The test above reads through an
    /// administrator's context; here each context is built for its caller, as in production, and the
    /// administrator reads the same row over the same database as the inverse control, so the refusal is
    /// the check and not a missing row.
    /// </remarks>
    [Fact]
    public async Task A_caller_that_is_neither_administrator_nor_reseller_is_refused_by_the_directory_s_scope_check()
    {
        var database = Guid.NewGuid().ToString();
        var reseller = ResellerTestData.Reseller();
        await using (var seed = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin()))
        {
            seed.Resellers.Add(reseller);
            await seed.SaveChangesAsync();
        }

        ICurrentUser[] callers =
        [
            FakeCurrentUser.Customer(Guid.NewGuid()),
            FakeCurrentUser.AccountlessCustomer(),
            FakeCurrentUser.Anonymous(),
        ];
        foreach (var caller in callers)
        {
            await using var dbContext = AccountsTestContext.CreateOn(database, caller);

            Assert.Null(await new ResellerDirectory(dbContext, caller).FindAsync(reseller.Id, CancellationToken.None));
        }

        var admin = FakeCurrentUser.Admin();
        await using var adminContext = AccountsTestContext.CreateOn(database, admin);

        Assert.NotNull(await new ResellerDirectory(adminContext, admin).FindAsync(reseller.Id, CancellationToken.None));
    }

    /// <summary>An administrator asking for an id that does not exist is answered null.</summary>
    [Fact]
    public async Task An_unknown_id_is_answered_null_for_an_administrator()
    {
        await using var dbContext = AccountsTestContext.Create();
        var directory = new ResellerDirectory(dbContext, FakeCurrentUser.Admin());

        Assert.Null(await directory.FindAsync(Guid.NewGuid(), CancellationToken.None));
    }

    /// <summary>Seeds resellers into a context filtered for <paramref name="currentUser"/>.</summary>
    /// <param name="currentUser">The principal the context is built for.</param>
    /// <param name="resellers">The resellers to add.</param>
    /// <returns>The context, which the caller disposes.</returns>
    private static async Task<AccountsDbContext> SeedAsync(
        FakeCurrentUser currentUser,
        params Reseller[] resellers)
    {
        var dbContext = AccountsTestContext.Create(currentUser);
        dbContext.Resellers.AddRange(resellers);
        await dbContext.SaveChangesAsync();
        return dbContext;
    }
}
