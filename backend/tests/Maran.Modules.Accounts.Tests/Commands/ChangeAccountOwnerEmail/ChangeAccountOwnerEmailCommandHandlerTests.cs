using Maran.Modules.Accounts.Commands.ChangeAccountOwnerEmail;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.ChangeAccountOwnerEmail;

/// <summary>
/// Behavioral contract of <see cref="ChangeAccountOwnerEmailCommandHandler"/>: the login is asked to
/// follow the new address first, and only an accepted change reaches the account's own row.
/// </summary>
public sealed class ChangeAccountOwnerEmailCommandHandlerTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The plan the account is on.</summary>
    private readonly Plan _plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>The account every test changes.</summary>
    private readonly Account _account;

    /// <summary>Stores the plan and the account.</summary>
    public ChangeAccountOwnerEmailCommandHandlerTests()
    {
        _dbContext = AccountsTestContext.Create();
        _dbContext.Plans.Add(_plan);
        _account = new Account(
            Guid.NewGuid(),
            "olive",
            "olive.example.com",
            _plan.Id,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "old@example.com");
        _dbContext.Accounts.Add(_account);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Builds the handler over this test's context and journal and the given bus.</summary>
    /// <param name="bus">The bus the login's change is requested on.</param>
    private ChangeAccountOwnerEmailCommandHandler NewHandler(StubMessageBus bus)
    {
        return new ChangeAccountOwnerEmailCommandHandler(
            _dbContext,
            bus,
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            new StubDisplayNames());
    }

    /// <summary>Builds a bus whose login answers every request with <paramref name="answer"/>.</summary>
    /// <param name="answer">What the login module answers.</param>
    private static StubMessageBus LoginAnswering(Result<bool> answer)
    {
        return new StubMessageBus
        {
            Responder = _ =>
            {
                return answer;
            },
        };
    }

    /// <summary>Builds a change of the test account's address.</summary>
    /// <param name="email">The new address.</param>
    /// <param name="accountId">The account to change; the test account unless a test aims elsewhere.</param>
    private ChangeAccountOwnerEmailCommand ChangeTo(string email, Guid? accountId = null)
    {
        return new ChangeAccountOwnerEmailCommand(email, accountId ?? _account.Id, "203.0.113.7", "unit-tests");
    }

    /// <summary>Re-reads the account's owner address from the store.</summary>
    private async Task<string?> StoredEmailAsync()
    {
        return (await _dbContext.Accounts.AsNoTracking().SingleAsync(a => a.Id == _account.Id)).OwnerEmail;
    }

    /// <summary>An accepted change asks the login to follow, stores the address and is journalled without it.</summary>
    [Fact]
    public async Task An_accepted_change_asks_the_login_to_follow_stores_the_address_and_is_journalled_without_it()
    {
        var bus = LoginAnswering(Result<bool>.Ok(true));

        var result = await NewHandler(bus).HandleAsync(ChangeTo("new@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new@example.com", result.Value.OwnerEmail);
        Assert.Equal("new@example.com", await StoredEmailAsync());
        var request = Assert.IsType<AccountLoginEmailChangeRequested>(Assert.Single(bus.Invoked));
        Assert.Equal(_account.Id, request.AccountId);
        Assert.Equal("new@example.com", request.Email);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AccountOwnerEmailChanged, entry.Action);
        Assert.Equal("olive", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain("example.com", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A change the login refuses keeps the stored address and answers with the login's refusal.</summary>
    [Fact]
    public async Task A_change_the_login_refuses_keeps_the_stored_address_and_answers_with_the_logins_refusal()
    {
        var bus = LoginAnswering(Result<bool>.Fail(Error.Of("EmailTaken", ErrorType.Conflict)));

        var result = await NewHandler(bus).HandleAsync(ChangeTo("taken@example.com"), CancellationToken.None);

        Assert.Equal("EmailTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("old@example.com", await StoredEmailAsync());
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A change of an account that does not exist is refused as not found without asking the login.</summary>
    [Fact]
    public async Task A_change_of_an_account_that_does_not_exist_is_refused_as_not_found_without_asking_the_login()
    {
        var bus = LoginAnswering(Result<bool>.Ok(true));
        var missing = Guid.NewGuid();

        var result = await NewHandler(bus).HandleAsync(ChangeTo("new@example.com", missing), CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(bus.Invoked);
        Assert.Equal(missing.ToString("D"), Assert.Single(_audit.Entries).Subject);
    }
}
