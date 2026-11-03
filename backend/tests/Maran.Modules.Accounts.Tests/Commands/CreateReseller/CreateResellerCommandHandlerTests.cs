using Maran.Modules.Accounts.Commands.CreateReseller;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreateReseller;

/// <summary>
/// Behavioral contract of <see cref="CreateResellerCommandHandler"/>: the row is stored, Identity is
/// asked for the login through <see cref="ResellerCreated"/>, a taken name is refused, and a login
/// that could not be issued is reported without rolling the reseller back.
/// </summary>
public sealed class CreateResellerCommandHandlerTests : IDisposable
{
    /// <summary>The instant the clock reports.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The handler's log.</summary>
    private readonly RecordingLogger<CreateResellerCommandHandler> _logger = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Builds the handler over this test's context and the given bus.</summary>
    /// <param name="bus">The bus the login is requested on.</param>
    private CreateResellerCommandHandler NewHandler(StubMessageBus bus)
    {
        return new CreateResellerCommandHandler(
            _dbContext,
            new FakeClock(Now),
            new AccountAuditJournal(_audit, new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: true, "boss")),
            bus,
            _logger,
            new StubDisplayNames().With("ResellerStatusActive", "Live"));
    }

    /// <summary>Builds a creation of the reseller <paramref name="name"/>.</summary>
    /// <param name="name">The reseller's name.</param>
    private static CreateResellerCommand Create(string name = "acme")
    {
        return new CreateResellerCommand(
            name,
            "owner@acme.example",
            ResellerEnvelopeDtoTestData.Small(maxAccounts: 3, diskPoolMb: 5_000, maxPlans: 2),
            "ru",
            "203.0.113.7",
            "unit-tests");
    }

    /// <summary>A creation stores an active reseller with its envelope, contact, language and time.</summary>
    [Fact]
    public async Task A_creation_stores_an_active_reseller_with_its_envelope_contact_language_and_time()
    {
        var result = await NewHandler(new StubMessageBus()).HandleAsync(Create(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await _dbContext.Resellers.AsNoTracking().SingleAsync();
        Assert.Equal(result.Value.Id, stored.Id);
        Assert.Equal("acme", stored.Name);
        Assert.Equal("owner@acme.example", stored.ContactEmail);
        Assert.Equal("ru", stored.Language);
        Assert.Equal(ResellerStatus.Active, stored.Status);
        Assert.Equal(Now, stored.CreatedAt);
        Assert.Equal(3, stored.Envelope.MaxAccounts);
        Assert.Equal(5_000, stored.Envelope.DiskPoolMb);
        Assert.Equal(2, stored.Envelope.MaxPlans);
    }

    /// <summary>The response is the detail with no usage yet and the localized state name.</summary>
    [Fact]
    public async Task The_response_is_the_detail_with_no_usage_and_the_localized_state_name()
    {
        var result = await NewHandler(new StubMessageBus()).HandleAsync(Create(), CancellationToken.None);

        Assert.Equal("Live", result.Value.StatusName);
        Assert.Equal(0, result.Value.AccountCount);
        Assert.Equal(0, result.Value.PlanCount);
        Assert.Equal(0, result.Value.DiskAllocatedMb);
        Assert.Equal(3, result.Value.Envelope.MaxAccounts);
    }

    /// <summary>A creation asks Identity for the login with the reseller's id, name, contact and language.</summary>
    [Fact]
    public async Task A_creation_asks_identity_for_the_login_with_the_resellers_id_name_contact_and_language()
    {
        var bus = new StubMessageBus();

        var result = await NewHandler(bus).HandleAsync(Create(), CancellationToken.None);

        var created = Assert.IsType<ResellerCreated>(Assert.Single(bus.Invoked));
        Assert.Equal(result.Value.Id, created.ResellerId);
        Assert.Equal("acme", created.Username);
        Assert.Equal("owner@acme.example", created.OwnerEmail);
        Assert.Equal("ru", created.OwnerLanguage);
    }

    /// <summary>A creation is journalled as a success naming the reseller, with the caller's address.</summary>
    [Fact]
    public async Task A_creation_is_journalled_as_a_success_naming_the_reseller()
    {
        await NewHandler(new StubMessageBus()).HandleAsync(Create(), CancellationToken.None);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AccountAuditActions.ResellerCreated, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.Equal("boss", entry.ActorUsername);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Equal("unit-tests", entry.UserAgent);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A name another reseller holds is refused as taken and journalled as a failure.</summary>
    [Fact]
    public async Task A_name_another_reseller_holds_is_refused_as_taken()
    {
        _dbContext.Resellers.Add(ResellerTestData.Reseller("acme"));
        await _dbContext.SaveChangesAsync();
        var bus = new StubMessageBus();

        var result = await NewHandler(bus).HandleAsync(Create(), CancellationToken.None);

        Assert.Equal("ResellerNameTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(1, await _dbContext.Resellers.CountAsync());
        Assert.Empty(bus.Invoked);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AccountAuditActions.ResellerCreated, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A name an account holds is refused with the same code as a reseller's name.</summary>
    [Fact]
    public async Task A_name_an_account_holds_is_refused_with_the_same_code()
    {
        _dbContext.Accounts.Add(new Account(
            Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), ResellerTestData.CreatedAt));
        await _dbContext.SaveChangesAsync();
        var bus = new StubMessageBus();

        var result = await NewHandler(bus).HandleAsync(Create(), CancellationToken.None);

        Assert.Equal("ResellerNameTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(0, await _dbContext.Resellers.CountAsync());
        Assert.Empty(bus.Invoked);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A different name is created beside an existing reseller, the inverse control.</summary>
    [Fact]
    public async Task A_different_name_is_created_beside_an_existing_reseller()
    {
        _dbContext.Resellers.Add(ResellerTestData.Reseller("other"));
        await _dbContext.SaveChangesAsync();

        var result = await NewHandler(new StubMessageBus()).HandleAsync(Create(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await _dbContext.Resellers.CountAsync());
    }

    /// <summary>A login that could not be issued is reported with its own code and the reseller is kept.</summary>
    [Fact]
    public async Task A_login_that_could_not_be_issued_is_reported_with_its_own_code_and_the_reseller_is_kept()
    {
        var bus = new StubMessageBus(new InvalidOperationException("secret subscriber detail"));

        var result = await NewHandler(bus).HandleAsync(Create(), CancellationToken.None);

        Assert.Equal("ResellerLoginIssuanceFailed", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal(1, await _dbContext.Resellers.CountAsync());
        var entry = Assert.Single(_audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain("secret subscriber detail", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A login that could not be issued is logged for the operator by reseller name.</summary>
    [Fact]
    public async Task A_login_that_could_not_be_issued_is_logged_for_the_operator()
    {
        var bus = new StubMessageBus(new InvalidOperationException("boom"));

        await NewHandler(bus).HandleAsync(Create(), CancellationToken.None);

        Assert.Contains(_logger.Messages, message =>
        {
            return message.Contains("acme", StringComparison.Ordinal);
        });
    }
}
