using Maran.Modules.Accounts.Commands.ReactivateReseller;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.ReactivateReseller;

/// <summary>
/// Behavioral contract of <see cref="ReactivateResellerCommandHandler"/>: a suspended reseller is made
/// active and its login is asked to follow, repeating it is harmless, an unknown reseller is not
/// found, and the accounts it owns are not touched.
/// </summary>
public sealed class ReactivateResellerCommandHandlerTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The bus the login's resumption is requested on.</summary>
    private readonly StubMessageBus _bus = new();

    /// <summary>The suspended reseller every test reactivates.</summary>
    private readonly Reseller _reseller = ResellerTestData.Reseller("acme");

    /// <summary>Stores the reseller, suspended.</summary>
    public ReactivateResellerCommandHandlerTests()
    {
        _reseller.Suspend();
        _dbContext.Resellers.Add(_reseller);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Runs the handler over this test's context and bus.</summary>
    /// <param name="resellerId">The reseller to reactivate; the test reseller when null.</param>
    private Task<Result<ResellerDetailDto>> RunAsync(Guid? resellerId = null)
    {
        var handler = new ReactivateResellerCommandHandler(
            _dbContext,
            _bus,
            new ResellerUsageReader(_dbContext),
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            new StubDisplayNames().With("ResellerStatusActive", "Live"));
        return handler.HandleAsync(
            new ReactivateResellerCommand(resellerId ?? _reseller.Id, "203.0.113.7", "unit-tests"),
            CancellationToken.None);
    }

    /// <summary>A reactivation stores the active state, answers it and asks the login to follow.</summary>
    [Fact]
    public async Task A_reactivation_stores_the_state_answers_it_and_asks_the_login_to_follow()
    {
        var result = await RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(ResellerStatus.Active, result.Value.Status);
        Assert.Equal("Live", result.Value.StatusName);
        Assert.Equal(ResellerStatus.Active, (await _dbContext.Resellers.AsNoTracking().SingleAsync()).Status);
        var resuming = Assert.IsType<ResellerResuming>(Assert.Single(_bus.Invoked));
        Assert.Equal(_reseller.Id, resuming.ResellerId);
    }

    /// <summary>A reactivation is journalled as a success naming the reseller.</summary>
    [Fact]
    public async Task A_reactivation_is_journalled_as_a_success_naming_the_reseller()
    {
        await RunAsync();

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AccountAuditActions.ResellerReactivated, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Reactivating a reseller that is already active succeeds and leaves it active.</summary>
    [Fact]
    public async Task Reactivating_a_reseller_that_is_already_active_succeeds()
    {
        await RunAsync();

        var again = await RunAsync();

        Assert.True(again.IsSuccess);
        Assert.Equal(ResellerStatus.Active, (await _dbContext.Resellers.AsNoTracking().SingleAsync()).Status);
    }

    /// <summary>A reseller that does not exist is refused as not found, asks nobody and is journalled.</summary>
    [Fact]
    public async Task A_reseller_that_does_not_exist_is_refused_as_not_found()
    {
        var missing = Guid.NewGuid();

        var result = await RunAsync(missing);

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_bus.Invoked);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(missing.ToString(), entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Reactivating a reseller does not reactivate a suspended account it owns.</summary>
    [Fact]
    public async Task Reactivating_a_reseller_does_not_reactivate_a_suspended_account_it_owns()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Basic", 1_000, 1, 1, 1, 0, 0, 1, resellerId: _reseller.Id);
        var paused = new Account(
            Guid.NewGuid(),
            "paused",
            "paused.example.com",
            plan.Id,
            ResellerTestData.CreatedAt,
            resellerId: _reseller.Id);
        paused.Suspend(SuspensionActor.Administrator);
        _dbContext.Plans.Add(plan);
        _dbContext.Accounts.Add(paused);
        await _dbContext.SaveChangesAsync();

        await RunAsync();

        Assert.Equal(AccountStatus.Suspended, (await _dbContext.Accounts.AsNoTracking().SingleAsync()).Status);
        Assert.IsType<ResellerResuming>(Assert.Single(_bus.Invoked));
    }
}
