using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ListAuditEvents;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Identity.Tests.Queries.ListAuditEvents;

/// <summary>
/// What a reseller reads of the journal (issue #370): the entries about the accounts it owns and the
/// entries its own login made, and nothing else — and a 404 for an account filter outside that.
/// </summary>
public sealed class ListAuditEventsResellerScopeTests : IDisposable
{
    /// <summary>The instant the seeded entries count from.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory journal the handler reads.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The reseller login that reads.</summary>
    private readonly Guid _loginId = Guid.NewGuid();

    /// <summary>An account the reading reseller owns.</summary>
    private readonly Guid _ownAccount = Guid.NewGuid();

    /// <summary>An account some other reseller (or the administrator) owns.</summary>
    private readonly Guid _foreignAccount = Guid.NewGuid();

    /// <summary>The Accounts window answering which accounts the reseller owns.</summary>
    private readonly StubResellerDirectory _directory = new();

    /// <summary>Creates the fixture: the reseller owns one account.</summary>
    public ListAuditEventsResellerScopeTests()
    {
        _directory.AddOwnedAccount(_ownAccount);
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Only entries of the reseller's own accounts and its own login are listed; another's are not.</summary>
    [Fact]
    public async Task A_reseller_sees_its_accounts_entries_and_its_own_logins_and_nothing_else()
    {
        await WriteAsync("own-account", accountId: _ownAccount);
        await WriteAsync("own-login", actorUserId: _loginId);
        await WriteAsync("foreign-account", accountId: _foreignAccount);
        await WriteAsync("server-wide");
        await WriteAsync("same-name-other-user", actor: "reseller", actorUserId: Guid.NewGuid());

        var result = await HandlerFor(new StubResellerUser(_loginId, "reseller", Guid.NewGuid()))
            .HandleAsync(new ListAuditEventsQuery(), CancellationToken.None);

        string[] expected = ["own-account", "own-login"];
        Assert.Equal(2, result.Value.Total);
        Assert.Equivalent(expected, result.Value.Items.Select(row =>
        {
            return row.Subject;
        }));
    }

    /// <summary>The inverse control: an administrator over the same journal sees all five, so the narrowing above is the scope's doing.</summary>
    [Fact]
    public async Task An_administrator_over_the_same_journal_sees_every_entry()
    {
        await WriteAsync("own-account", accountId: _ownAccount);
        await WriteAsync("foreign-account", accountId: _foreignAccount);
        await WriteAsync("server-wide");

        var result = await HandlerFor(new StubCurrentUser()).HandleAsync(new ListAuditEventsQuery(), CancellationToken.None);

        Assert.Equal(3, result.Value.Total);
    }

    /// <summary>Filtering by an owned account keeps only that account's entries.</summary>
    [Fact]
    public async Task Filtering_by_an_owned_account_keeps_that_accounts_entries()
    {
        await WriteAsync("own-account", accountId: _ownAccount);
        await WriteAsync("own-login-elsewhere", actorUserId: _loginId);

        var result = await HandlerFor(new StubResellerUser(_loginId, "reseller", Guid.NewGuid()))
            .HandleAsync(new ListAuditEventsQuery(Account: _ownAccount), CancellationToken.None);

        Assert.Equal(["own-account"], result.Value.Items.Select(row =>
        {
            return row.Subject;
        }));
    }

    /// <summary>An account the reseller does not own answers 404, the same as one that does not exist.</summary>
    [Fact]
    public async Task Filtering_by_an_account_it_does_not_own_is_a_not_found()
    {
        await WriteAsync("foreign-account", accountId: _foreignAccount);
        var handler = HandlerFor(new StubResellerUser(_loginId, "reseller", Guid.NewGuid()));

        var foreign = await handler.HandleAsync(new ListAuditEventsQuery(Account: _foreignAccount), CancellationToken.None);
        var missing = await handler.HandleAsync(new ListAuditEventsQuery(Account: Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("AuditAccountNotFound", foreign.Error!.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(foreign.Error, missing.Error);
    }

    /// <summary>A customer dispatching the query is refused rather than shown the journal.</summary>
    [Fact]
    public async Task A_customer_is_refused()
    {
        await WriteAsync("own-account", accountId: _ownAccount);

        var result = await HandlerFor(new StubCustomerUser(Guid.NewGuid(), "customer", _ownAccount))
            .HandleAsync(new ListAuditEventsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AuditJournalNotReadable", result.Error!.Code);
    }

    /// <summary>Creates the handler under test for a caller.</summary>
    /// <param name="user">The caller.</param>
    /// <returns>The handler.</returns>
    private ListAuditEventsQueryHandler HandlerFor(ICurrentUser user)
    {
        return new ListAuditEventsQueryHandler(
            new AuditEventQuery(_context),
            new AuditEventScopeReader(user, _directory),
            IdentityTestContext.ActionNames(),
            IdentityTestContext.OutcomeNames(),
            IdentityTestContext.SubjectNames());
    }

    /// <summary>Records one entry.</summary>
    /// <param name="subject">What it was done to; the tests identify rows by it.</param>
    /// <param name="accountId">The account the entry is stamped with, or <c>null</c> for none.</param>
    /// <param name="actorUserId">The user who did it, or <c>null</c> for none.</param>
    /// <param name="actor">The name the actor used.</param>
    /// <returns>Resolves once the entry is saved.</returns>
    private async Task WriteAsync(string subject, Guid? accountId = null, Guid? actorUserId = null, string actor = "someone")
    {
        _context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), Now, actorUserId, actor, "LoginSucceeded", subject, "203.0.113.7", "agent", true, null, null, accountId));
        await _context.SaveChangesAsync();
    }
}
