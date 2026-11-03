using Maran.Modules.Accounts.Commands.SuspendReseller;
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

namespace Maran.Modules.Accounts.Tests.Commands.SuspendReseller;

/// <summary>
/// Behavioral contract of <see cref="SuspendResellerCommandHandler"/>: the reseller is suspended and
/// its login is asked to follow, repeating it is harmless, an unknown reseller is not found, and (D2)
/// not one account the reseller owns is touched.
/// </summary>
public sealed class SuspendResellerCommandHandlerTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The bus the login's suspension is requested on.</summary>
    private readonly StubMessageBus _bus = new();

    /// <summary>The reseller every test suspends.</summary>
    private readonly Reseller _reseller = ResellerTestData.Reseller("acme");

    /// <summary>Stores the reseller.</summary>
    public SuspendResellerCommandHandlerTests()
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
    /// <param name="resellerId">The reseller to suspend; the test reseller when null.</param>
    private Task<Result<ResellerDetailDto>> RunAsync(Guid? resellerId = null)
    {
        var handler = new SuspendResellerCommandHandler(
            _dbContext,
            _bus,
            new ResellerUsageReader(_dbContext),
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            new StubDisplayNames().With("ResellerStatusSuspended", "Paused"));
        return handler.HandleAsync(
            new SuspendResellerCommand(resellerId ?? _reseller.Id, "203.0.113.7", "unit-tests"),
            CancellationToken.None);
    }

    /// <summary>A suspension stores the suspended state, answers it and asks the login to follow.</summary>
    [Fact]
    public async Task A_suspension_stores_the_state_answers_it_and_asks_the_login_to_follow()
    {
        var result = await RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(ResellerStatus.Suspended, result.Value.Status);
        Assert.Equal("Paused", result.Value.StatusName);
        Assert.Equal(ResellerStatus.Suspended, (await _dbContext.Resellers.AsNoTracking().SingleAsync()).Status);
        var suspending = Assert.IsType<ResellerSuspending>(Assert.Single(_bus.Invoked));
        Assert.Equal(_reseller.Id, suspending.ResellerId);
    }

    /// <summary>A suspension is journalled as a success naming the reseller.</summary>
    [Fact]
    public async Task A_suspension_is_journalled_as_a_success_naming_the_reseller()
    {
        await RunAsync();

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AccountAuditActions.ResellerSuspended, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Suspending a reseller that is already suspended succeeds and leaves it suspended.</summary>
    [Fact]
    public async Task Suspending_a_reseller_that_is_already_suspended_succeeds()
    {
        await RunAsync();

        var again = await RunAsync();

        Assert.True(again.IsSuccess);
        Assert.Equal(ResellerStatus.Suspended, (await _dbContext.Resellers.AsNoTracking().SingleAsync()).Status);
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

    /// <summary>
    /// D2: suspending a reseller leaves every account it owns exactly as it was and asks nobody to
    /// suspend one. The only message is the login's, and the accounts keep their state and fields.
    /// </summary>
    [Fact]
    public async Task Suspending_a_reseller_does_not_touch_the_accounts_it_owns()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Basic", 1_000, 1, 1, 1, 0, 0, 1, resellerId: _reseller.Id);
        var running = OwnedAccount("running", plan);
        var paused = OwnedAccount("paused", plan);
        paused.Suspend();
        _dbContext.Plans.Add(plan);
        _dbContext.Accounts.AddRange(running, paused);
        await _dbContext.SaveChangesAsync();

        await RunAsync();

        var accounts = await _dbContext.Accounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync();
        Assert.Equal(2, accounts.Count);
        Assert.Equal(AccountStatus.Suspended, accounts[0].Status);
        Assert.Equal("paused", accounts[0].Name);
        Assert.Equal(AccountStatus.Active, accounts[1].Status);
        Assert.Equal("running", accounts[1].Name);
        Assert.All(accounts, account =>
        {
            Assert.Equal(_reseller.Id, account.ResellerId);
            Assert.Equal(plan.Id, account.PlanId);
        });
        Assert.IsType<ResellerSuspending>(Assert.Single(_bus.Invoked));
    }

    /// <summary>Builds an account of the test reseller on <paramref name="plan"/>.</summary>
    /// <param name="name">The account's name.</param>
    /// <param name="plan">The plan the account is on.</param>
    private Account OwnedAccount(string name, Plan plan)
    {
        return new Account(
            Guid.NewGuid(),
            name,
            $"{name}.example.com",
            plan.Id,
            ResellerTestData.CreatedAt,
            resellerId: _reseller.Id);
    }
}
