using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Which hosting account <see cref="DatabaseAuditWriter"/> stamps on an entry: the one the entry
/// carries, otherwise the one a customer session acts for, otherwise none.
/// </summary>
public sealed class DatabaseAuditWriterAccountTests : IDisposable
{
    /// <summary>The instant the fixed clock reports.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory journal the writer appends to.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>An entry written by a customer session is stamped with the account the session acts for.</summary>
    [Fact]
    public async Task An_entry_written_by_a_customer_is_stamped_with_the_account_it_acts_for()
    {
        var accountId = Guid.NewGuid();
        var customer = new StubCustomerUser(Guid.NewGuid(), "olive", accountId);

        await WriteAsync(customer, Entry());

        Assert.Equal(accountId, Assert.Single(_context.AuditEvents).AccountId);
    }

    /// <summary>An entry written while an operator views as a customer is stamped with the viewed account.</summary>
    [Fact]
    public async Task An_entry_written_while_viewing_as_a_customer_is_stamped_with_the_viewed_account()
    {
        var viewing = new ViewingAsCurrentUser(Guid.NewGuid(), "olive", Guid.NewGuid(), "root-admin");

        await WriteAsync(viewing, Entry());

        Assert.Equal(((CustomerCaller)viewing.Caller).AccountId, Assert.Single(_context.AuditEvents).AccountId);
    }

    /// <summary>
    /// An administrator, an account-less customer and an anonymous caller supply no account, so the
    /// column stays empty — the inverse control for the stamps above.
    /// </summary>
    [Fact]
    public async Task An_administrator_an_accountless_customer_and_an_anonymous_caller_stamp_no_account()
    {
        await WriteAsync(new StubCurrentUser(), Entry());
        await WriteAsync(new StubCustomerUser(Guid.NewGuid(), "olive", accountId: null), Entry());
        await WriteAsync(new AnonymousUser(), Entry());

        Assert.All(_context.AuditEvents, auditEvent =>
        {
            Assert.Null(auditEvent.AccountId);
        });
        Assert.Equal(3, _context.AuditEvents.Count());
    }

    /// <summary>An account the entry carries itself wins over the one the session acts for.</summary>
    [Fact]
    public async Task An_account_the_entry_carries_wins_over_the_sessions()
    {
        var sessionAccount = Guid.NewGuid();
        var entryAccount = Guid.NewGuid();
        var customer = new StubCustomerUser(Guid.NewGuid(), "olive", sessionAccount);

        await WriteAsync(customer, Entry() with { AccountId = entryAccount });

        Assert.Equal(entryAccount, Assert.Single(_context.AuditEvents).AccountId);
    }

    /// <summary>An account the entry carries is stamped even when the session supplies none.</summary>
    [Fact]
    public async Task An_account_the_entry_carries_is_stamped_for_an_administrator()
    {
        var entryAccount = Guid.NewGuid();

        await WriteAsync(new StubCurrentUser(), Entry() with { AccountId = entryAccount });

        Assert.Equal(entryAccount, Assert.Single(_context.AuditEvents).AccountId);
    }

    /// <summary>Builds an entry that carries no account of its own.</summary>
    /// <returns>The entry.</returns>
    private static AuditEntry Entry()
    {
        return new AuditEntry(
            Guid.NewGuid(), "olive", IdentityAuditActions.SessionRevoked, "subject", "203.0.113.7", "agent", Succeeded: true);
    }

    /// <summary>Writes one entry as the given principal through a fresh writer.</summary>
    /// <param name="currentUser">The principal the writer reads.</param>
    /// <param name="entry">The entry to write.</param>
    /// <returns>Resolves once the entry is saved.</returns>
    private async Task WriteAsync(ICurrentUser currentUser, AuditEntry entry)
    {
        var writer = new DatabaseAuditWriter(_context, new FakeClock(Now), new FakeCorrelationIdAccessor(null), currentUser);

        await writer.WriteAsync(entry, CancellationToken.None);
    }

    /// <summary>An <see cref="ICurrentUser"/> double for a request with no signed-in principal.</summary>
    private sealed class AnonymousUser : ICurrentUser
    {
        /// <inheritdoc />
        public Caller Caller
        {
            get { return AnonymousCaller.Instance; }
        }
    }
}
