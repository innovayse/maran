using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>What <see cref="ResellerNameDirectory"/> names, and what it leaves unnamed.</summary>
public sealed class ResellerNameDirectoryTests
{
    /// <summary>An owner id of null is the administrator's own and has no name.</summary>
    [Fact]
    public async Task The_administrators_own_has_no_name()
    {
        await using var dbContext = AccountsTestContext.Create();
        var directory = new ResellerNameDirectory(dbContext);

        Assert.Null(await directory.NameOfAsync(null, CancellationToken.None));
    }

    /// <summary>A reseller is named by its id.</summary>
    [Fact]
    public async Task A_reseller_is_named_by_its_id()
    {
        await using var dbContext = AccountsTestContext.Create();
        var reseller = ResellerTestData.Reseller("acme");
        dbContext.Resellers.Add(reseller);
        await dbContext.SaveChangesAsync();
        var directory = new ResellerNameDirectory(dbContext);

        Assert.Equal("acme", await directory.NameOfAsync(reseller.Id, CancellationToken.None));
    }

    /// <summary>An id that names no reseller reads as no name.</summary>
    [Fact]
    public async Task An_unknown_id_reads_as_no_name()
    {
        await using var dbContext = AccountsTestContext.Create();
        var directory = new ResellerNameDirectory(dbContext);

        Assert.Null(await directory.NameOfAsync(Guid.NewGuid(), CancellationToken.None));
    }

    /// <summary>Several resellers are named in one read, duplicates and unknown ids included.</summary>
    [Fact]
    public async Task Several_resellers_are_named_in_one_read()
    {
        await using var dbContext = AccountsTestContext.Create();
        var first = ResellerTestData.Reseller("first");
        var second = ResellerTestData.Reseller("second");
        dbContext.Resellers.AddRange(first, second);
        await dbContext.SaveChangesAsync();
        var directory = new ResellerNameDirectory(dbContext);

        var names = await directory.NamesOfAsync(
            [first.Id, second.Id, first.Id, Guid.NewGuid()],
            CancellationToken.None);

        Assert.Equal(2, names.Count);
        Assert.Equal("first", names[first.Id]);
        Assert.Equal("second", names[second.Id]);
    }

    /// <summary>Asking about nobody answers nothing.</summary>
    [Fact]
    public async Task Asking_about_nobody_answers_nothing()
    {
        await using var dbContext = AccountsTestContext.Create();
        var directory = new ResellerNameDirectory(dbContext);

        Assert.Empty(await directory.NamesOfAsync([], CancellationToken.None));
    }

    /// <summary>A reseller is shown its own name and not another reseller's: the context's narrowing applies.</summary>
    [Fact]
    public async Task A_reseller_is_shown_its_own_name_and_not_anothers()
    {
        var database = Guid.NewGuid().ToString();
        var own = ResellerTestData.Reseller("mine");
        var other = ResellerTestData.Reseller("theirs");
        await using (var seed = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin()))
        {
            seed.Resellers.AddRange(own, other);
            await seed.SaveChangesAsync();
        }

        await using var dbContext = AccountsTestContext.CreateOn(database, FakeCurrentUser.Reseller(own.Id));
        var directory = new ResellerNameDirectory(dbContext);

        Assert.Equal("mine", await directory.NameOfAsync(own.Id, CancellationToken.None));
        Assert.Null(await directory.NameOfAsync(other.Id, CancellationToken.None));
    }
}
