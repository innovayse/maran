using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Accounts.Tests.Commands.DeleteAccount;

/// <summary>
/// What deleting an account leaves in the audit journal. Deletion destroys the system user, the
/// home directory, every database the account owned and every SFTP login it owned, and then removes
/// the row — after which the journal holds the only remaining record that the account existed.
/// </summary>
public sealed class DeleteAccountAuditTests : IDisposable
{
    private const string Ip = "203.0.113.7";
    private const string Client = "unit-tests";

    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly AccountsDbContext _context = CreateDbContext();
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>A completed deletion is journalled as a success naming the account.</summary>
    [Fact]
    public async Task A_completed_deletion_is_journalled_as_a_success_naming_the_account()
    {
        var account = await SeedAsync();

        var result = await DeleteAsync(new RecordingAgentAccountsClient(), new StubMessageBus(), account.Id);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AccountDeleted, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal(Ip, entry.IpAddress);
        Assert.Equal(Client, entry.UserAgent);
    }

    /// <summary>A deletion that got part way through the cascade is journalled as a failure.</summary>
    [Fact]
    public async Task A_deletion_that_got_part_way_through_the_cascade_is_journalled_as_a_failure()
    {
        // The cascade has already run — the account's databases and SFTP logins are gone — and then
        // the agent refused, so the account is still there. Recording this as a success would tell
        // an operator hunting for the destruction that a clean removal happened.
        var account = await SeedAsync();
        var bus = new StubMessageBus();

        var result = await DeleteAsync(
            new RecordingAgentAccountsClient(Error.Of("AgentSystemFailure")), bus, account.Id);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Single(bus.Invoked);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AccountDeleted, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.False(entry.Succeeded);
        Assert.Equal(1, await _context.Accounts.CountAsync());
    }

    /// <summary>A deletion a module refused is journalled as a failure naming the account.</summary>
    [Fact]
    public async Task A_deletion_a_module_refused_is_journalled_as_a_failure_naming_the_account()
    {
        var account = await SeedAsync();
        var bus = new StubMessageBus(new InvalidOperationException("a module still holds rows"));

        var result = await DeleteAsync(new RecordingAgentAccountsClient(), bus, account.Id);

        Assert.Equal("AccountCleanupFailed", result.Error!.Code);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AccountDeleted, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A deletion of an unknown account is journalled naming what was probed for.</summary>
    [Fact]
    public async Task A_deletion_of_an_unknown_account_is_journalled_naming_what_was_probed_for()
    {
        var probed = Guid.NewGuid();

        var result = await DeleteAsync(new RecordingAgentAccountsClient(), new StubMessageBus(), probed);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AccountDeleted, entry.Action);
        Assert.Equal(probed.ToString(), entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>The journalled subject is the account name and never the agents freed byte count.</summary>
    [Fact]
    public async Task The_journalled_subject_is_the_account_name_and_never_the_agents_freed_byte_count()
    {
        // The agent answers with how many bytes it freed and knows the home directory it removed;
        // neither is what an operator searches a journal for, and neither belongs in one.
        var account = await SeedAsync();

        var result = await DeleteAsync(new RecordingAgentAccountsClient(), new StubMessageBus(), account.Id);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal("acme", entry.Subject);
        Assert.DoesNotContain("/", entry.Subject, StringComparison.Ordinal);
        Assert.NotEqual(result.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.Subject);
    }

    /// <summary>Builds a fresh, isolated in-memory context.</summary>
    /// <returns>The context.</returns>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Seeds one active account named "acme".</summary>
    /// <returns>The seeded account.</returns>
    private async Task<Account> SeedAsync()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Now);
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    /// <summary>Runs one deletion.</summary>
    /// <param name="agent">The agent double to answer with.</param>
    /// <param name="bus">The bus the cascade is invoked on.</param>
    /// <param name="accountId">The account to delete.</param>
    /// <returns>The handler's result.</returns>
    private Task<Result<ulong>> DeleteAsync(
        RecordingAgentAccountsClient agent,
        StubMessageBus bus,
        Guid accountId)
    {
        var handler = new DeleteAccountCommandHandler(
            _context,
            agent,
            bus,
            NullLogger<DeleteAccountCommandHandler>.Instance,
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()));

        return handler.HandleAsync(new DeleteAccountCommand(accountId, Ip, Client), CancellationToken.None);
    }
}
