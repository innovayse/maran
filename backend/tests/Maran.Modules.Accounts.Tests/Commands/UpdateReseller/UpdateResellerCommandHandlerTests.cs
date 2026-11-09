using Maran.Modules.Accounts.Commands.UpdateReseller;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.UpdateReseller;

/// <summary>
/// Behavioral contract of <see cref="UpdateResellerCommandHandler"/>: the reseller is renamed and its
/// contact replaced after Identity moved the login, a taken name or a refused login changes nothing,
/// an unknown reseller is not found, and every outcome is journalled.
/// </summary>
public sealed class UpdateResellerCommandHandlerTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The reseller every test changes.</summary>
    private readonly Reseller _reseller = ResellerTestData.Reseller("acme");

    /// <summary>Stores the reseller.</summary>
    public UpdateResellerCommandHandlerTests()
    {
        _dbContext.Resellers.Add(_reseller);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Runs the handler over this test's context.</summary>
    /// <param name="command">The change to apply.</param>
    /// <param name="bus">The bus the login's change is requested on.</param>
    private Task<Result<ResellerDetailDto>> RunAsync(UpdateResellerCommand command, StubMessageBus bus)
    {
        var handler = new UpdateResellerCommandHandler(
            _dbContext,
            new AccountNamespaceClaims(_dbContext),
            new ResellerUsageReader(_dbContext),
            bus,
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            new StubDisplayNames());
        return handler.HandleAsync(command, CancellationToken.None);
    }

    /// <summary>A bus whose Identity subscriber answers <paramref name="answer"/>.</summary>
    /// <param name="answer">What the login's change is answered with.</param>
    private static StubMessageBus BusAnswering(Result answer)
    {
        return new StubMessageBus { Responder = _ => { return answer; } };
    }

    /// <summary>Builds a change of the test reseller.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="email">The new contact address.</param>
    /// <param name="resellerId">The reseller to change; the test reseller unless a test aims elsewhere.</param>
    private UpdateResellerCommand Change(string name = "globex", string email = "new@acme.example", Guid? resellerId = null)
    {
        return new UpdateResellerCommand(name, email, "hy", resellerId ?? _reseller.Id, "203.0.113.7", "unit-tests");
    }

    /// <summary>A change renames the reseller, replaces its contact and answers the new state.</summary>
    [Fact]
    public async Task A_change_renames_the_reseller_replaces_its_contact_and_answers_the_new_state()
    {
        var result = await RunAsync(Change(), BusAnswering(Result.Ok()));

        Assert.True(result.IsSuccess);
        Assert.Equal("globex", result.Value.Name);
        Assert.Equal("hy", result.Value.Language);
        var stored = await _dbContext.Resellers.AsNoTracking().SingleAsync();
        Assert.Equal("globex", stored.Name);
        Assert.Equal("new@acme.example", stored.ContactEmail);
        Assert.Equal("hy", stored.Language);
    }

    /// <summary>A change asks Identity to move the login to the new name and address.</summary>
    [Fact]
    public async Task A_change_asks_identity_to_move_the_login_to_the_new_name_and_address()
    {
        var bus = BusAnswering(Result.Ok());

        await RunAsync(Change(), bus);

        var request = Assert.IsType<ResellerLoginChangeRequested>(Assert.Single(bus.Invoked));
        Assert.Equal(_reseller.Id, request.ResellerId);
        Assert.Equal("globex", request.Username);
        Assert.Equal("new@acme.example", request.Email);
    }

    /// <summary>A change is journalled as a success naming the reseller as it was called before.</summary>
    [Fact]
    public async Task A_change_is_journalled_as_a_success_naming_the_reseller_as_it_was_called_before()
    {
        await RunAsync(Change(), BusAnswering(Result.Ok()));

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AccountAuditActions.ResellerUpdated, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Keeping the name and changing only the contact is accepted, though the reseller holds the name.</summary>
    [Fact]
    public async Task Keeping_the_name_and_changing_only_the_contact_is_accepted()
    {
        var result = await RunAsync(Change(name: "acme"), BusAnswering(Result.Ok()));

        Assert.True(result.IsSuccess);
        var stored = await _dbContext.Resellers.AsNoTracking().SingleAsync();
        Assert.Equal("acme", stored.Name);
        Assert.Equal("new@acme.example", stored.ContactEmail);
    }

    /// <summary>A name another reseller holds is refused as taken and nothing changes.</summary>
    [Fact]
    public async Task A_name_another_reseller_holds_is_refused_as_taken_and_nothing_changes()
    {
        _dbContext.Resellers.Add(ResellerTestData.Reseller("globex"));
        await _dbContext.SaveChangesAsync();
        var bus = BusAnswering(Result.Ok());

        var result = await RunAsync(Change(), bus);

        Assert.Equal("ResellerNameTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(bus.Invoked);
        Assert.Equal("acme", (await _dbContext.Resellers.AsNoTracking().SingleAsync(r => r.Id == _reseller.Id)).Name);
        var entry = Assert.Single(_audit.Entries);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A name an account holds is refused as taken: one namespace with the account names.</summary>
    [Fact]
    public async Task A_name_an_account_holds_is_refused_as_taken()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Basic", 1_000, 1, 1, 1, 0, 0, 1);
        _dbContext.Plans.Add(plan);
        _dbContext.Accounts.Add(new Account(Guid.NewGuid(), "globex", "globex.example.com", plan.Id, ResellerTestData.CreatedAt));
        await _dbContext.SaveChangesAsync();

        var result = await RunAsync(Change(), BusAnswering(Result.Ok()));

        Assert.Equal("ResellerNameTaken", result.Error!.Code);
        Assert.Equal("acme", (await _dbContext.Resellers.AsNoTracking().SingleAsync()).Name);
    }

    /// <summary>A refusal from Identity is passed on and the reseller keeps its name and contact.</summary>
    [Fact]
    public async Task A_refusal_from_identity_is_passed_on_and_the_reseller_keeps_its_name_and_contact()
    {
        var refusal = Result.Fail(Error.Of("EmailTaken", ErrorType.Conflict));

        var result = await RunAsync(Change(), BusAnswering(refusal));

        Assert.Equal("EmailTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        var stored = await _dbContext.Resellers.AsNoTracking().SingleAsync();
        Assert.Equal("acme", stored.Name);
        Assert.Equal("acme@example.com", stored.ContactEmail);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A reseller that does not exist is refused as not found and journalled by the id asked for.</summary>
    [Fact]
    public async Task A_reseller_that_does_not_exist_is_refused_as_not_found()
    {
        var missing = Guid.NewGuid();
        var bus = BusAnswering(Result.Ok());

        var result = await RunAsync(Change(resellerId: missing), bus);

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(bus.Invoked);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(missing.ToString("D"), entry.Subject);
        Assert.False(entry.Succeeded);
    }
}
