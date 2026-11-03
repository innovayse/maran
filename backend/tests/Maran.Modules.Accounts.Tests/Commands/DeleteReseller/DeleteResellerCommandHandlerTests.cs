using Maran.Modules.Accounts.Commands.DeleteReseller;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.DeleteReseller;

/// <summary>
/// Behavioral contract of <see cref="DeleteResellerCommandHandler"/>: a reseller that owns nothing is
/// deleted and its login is asked to go, one that owns an account or a plan is refused, and an
/// unknown reseller is not found.
/// </summary>
public sealed class DeleteResellerCommandHandlerTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The bus the login's deletion is requested on.</summary>
    private readonly StubMessageBus _bus = new();

    /// <summary>The reseller every test deletes.</summary>
    private readonly Reseller _reseller = ResellerTestData.Reseller("acme");

    /// <summary>Stores the reseller.</summary>
    public DeleteResellerCommandHandlerTests()
    {
        _dbContext.Resellers.Add(_reseller);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Runs the handler over this test's context and bus.</summary>
    /// <param name="resellerId">The reseller to delete; the test reseller when null.</param>
    private Task<Result> RunAsync(Guid? resellerId = null)
    {
        var handler = new DeleteResellerCommandHandler(
            _dbContext,
            _bus,
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()));
        return handler.HandleAsync(
            new DeleteResellerCommand(resellerId ?? _reseller.Id, "203.0.113.7", "unit-tests"),
            CancellationToken.None);
    }

    /// <summary>A reseller that owns nothing is deleted, its login is asked to go, and it is journalled.</summary>
    [Fact]
    public async Task A_reseller_that_owns_nothing_is_deleted_and_its_login_is_asked_to_go()
    {
        var result = await RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(await _dbContext.Resellers.ToListAsync());
        var deleting = Assert.IsType<ResellerDeleting>(Assert.Single(_bus.Invoked));
        Assert.Equal(_reseller.Id, deleting.ResellerId);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AccountAuditActions.ResellerDeleted, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A reseller that owns an account is refused, kept, and journalled as a failure.</summary>
    [Fact]
    public async Task A_reseller_that_owns_an_account_is_refused_and_kept()
    {
        _dbContext.Accounts.Add(new Account(
            Guid.NewGuid(),
            "owned",
            "owned.example.com",
            Guid.NewGuid(),
            ResellerTestData.CreatedAt,
            resellerId: _reseller.Id));
        await _dbContext.SaveChangesAsync();

        var result = await RunAsync();

        Assert.Equal("ResellerHasAccounts", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(1, await _dbContext.Resellers.CountAsync());
        Assert.Empty(_bus.Invoked);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal("acme", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A reseller that owns a plan is refused with the same code and kept.</summary>
    [Fact]
    public async Task A_reseller_that_owns_a_plan_is_refused_and_kept()
    {
        _dbContext.Plans.Add(Plan.Define(Guid.NewGuid(), "Basic", 1_000, 1, 1, 1, 0, 0, 1, resellerId: _reseller.Id));
        await _dbContext.SaveChangesAsync();

        var result = await RunAsync();

        Assert.Equal("ResellerHasAccounts", result.Error!.Code);
        Assert.Equal(1, await _dbContext.Resellers.CountAsync());
        Assert.Empty(_bus.Invoked);
    }

    /// <summary>What another reseller owns does not stop this one being deleted: the inverse control.</summary>
    [Fact]
    public async Task What_another_reseller_owns_does_not_stop_this_one_being_deleted()
    {
        var other = ResellerTestData.Reseller("other");
        _dbContext.Resellers.Add(other);
        _dbContext.Plans.Add(Plan.Define(Guid.NewGuid(), "Theirs", 1_000, 1, 1, 1, 0, 0, 1, resellerId: other.Id));
        await _dbContext.SaveChangesAsync();

        var result = await RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(["other"], (await _dbContext.Resellers.ToListAsync()).Select(r => { return r.Name; }));
    }

    /// <summary>A reseller that does not exist is refused as not found and journalled by the id asked for.</summary>
    [Fact]
    public async Task A_reseller_that_does_not_exist_is_refused_as_not_found()
    {
        var missing = Guid.NewGuid();

        var result = await RunAsync(missing);

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_bus.Invoked);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(missing.ToString("D"), entry.Subject);
        Assert.False(entry.Succeeded);
    }
}
