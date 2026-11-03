using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.GetMyReseller;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.GetMyReseller;

/// <summary>
/// Behavioral contract of <see cref="GetMyResellerQueryHandler"/>: a reseller reads its own row with
/// its usage and nobody else's, and every other kind of caller is answered as not found.
/// </summary>
public sealed class GetMyResellerQueryHandlerTests : IDisposable
{
    /// <summary>The in-memory database the tests share between a seeding and a reading context.</summary>
    private readonly string _database = Guid.NewGuid().ToString();

    /// <summary>The contexts opened by a test, disposed with it.</summary>
    private readonly List<AccountsDbContext> _contexts = [];

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }
    }

    /// <summary>Opens a context over the shared database for <paramref name="user"/>.</summary>
    /// <param name="user">The principal the context's filters are evaluated for.</param>
    private AccountsDbContext Open(ICurrentUser user)
    {
        var context = AccountsTestContext.CreateOn(_database, user);
        _contexts.Add(context);
        return context;
    }

    /// <summary>Runs the handler as <paramref name="user"/>.</summary>
    /// <param name="user">The caller asking.</param>
    private Task<Result<ResellerDetailDto>> RunAsAsync(ICurrentUser user)
    {
        var context = Open(user);
        return new GetMyResellerQueryHandler(
                context,
                user,
                new ResellerUsageReader(context),
                new StubDisplayNames().With("ResellerStatusActive", "Live"))
            .HandleAsync(new GetMyResellerQuery(), CancellationToken.None);
    }

    /// <summary>Seeds two resellers, the first with one plan and one account.</summary>
    /// <returns>The two resellers.</returns>
    private async Task<(Reseller Mine, Reseller Other)> SeedAsync()
    {
        var mine = ResellerTestData.Reseller("mine", ResellerTestData.Envelope(maxAccounts: 6));
        var other = ResellerTestData.Reseller("other");
        var plan = Plan.Define(Guid.NewGuid(), "Basic", 1_500, 1, 1, 1, 0, 0, 1, resellerId: mine.Id);
        var seed = Open(FakeCurrentUser.Admin());
        seed.Resellers.AddRange(mine, other);
        seed.Plans.Add(plan);
        seed.Accounts.Add(new Account(
            Guid.NewGuid(),
            "owned",
            "owned.example.com",
            plan.Id,
            ResellerTestData.CreatedAt,
            resellerId: mine.Id));
        await seed.SaveChangesAsync();
        return (mine, other);
    }

    /// <summary>A reseller reads its own row with its envelope and usage.</summary>
    [Fact]
    public async Task A_reseller_reads_its_own_row_with_its_envelope_and_usage()
    {
        var (mine, _) = await SeedAsync();

        var result = await RunAsAsync(FakeCurrentUser.Reseller(mine.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(mine.Id, result.Value.Id);
        Assert.Equal("mine", result.Value.Name);
        Assert.Equal(6, result.Value.Envelope.MaxAccounts);
        Assert.Equal(1, result.Value.AccountCount);
        Assert.Equal(1, result.Value.PlanCount);
        Assert.Equal(1_500, result.Value.DiskAllocatedMb);
    }

    /// <summary>A reseller never reads another reseller's row: it reads its own, whatever else is stored.</summary>
    [Fact]
    public async Task A_reseller_reads_its_own_row_and_never_another_resellers()
    {
        var (_, other) = await SeedAsync();

        var result = await RunAsAsync(FakeCurrentUser.Reseller(other.Id));

        Assert.Equal("other", result.Value.Name);
        Assert.Equal(0, result.Value.AccountCount);
    }

    /// <summary>A reseller whose row no longer exists is answered as not found.</summary>
    [Fact]
    public async Task A_reseller_whose_row_no_longer_exists_is_answered_as_not_found()
    {
        await SeedAsync();

        var result = await RunAsAsync(FakeCurrentUser.Reseller(Guid.NewGuid()));

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>Every caller that is not a reseller is answered as not found.</summary>
    [Fact]
    public async Task Every_caller_that_is_not_a_reseller_is_answered_as_not_found()
    {
        await SeedAsync();
        ICurrentUser[] callers =
        [
            FakeCurrentUser.Admin(),
            FakeCurrentUser.Customer(Guid.NewGuid()),
            FakeCurrentUser.AccountlessCustomer(),
            FakeCurrentUser.Anonymous(),
        ];

        foreach (var caller in callers)
        {
            var result = await RunAsAsync(caller);

            Assert.Equal("ResellerNotFound", result.Error!.Code);
            Assert.Equal(ErrorType.NotFound, result.Error.Type);
        }
    }
}
