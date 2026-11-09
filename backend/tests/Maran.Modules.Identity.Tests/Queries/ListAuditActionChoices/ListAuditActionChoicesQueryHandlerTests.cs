using System.Globalization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ListAuditActionChoices;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Queries.ListAuditActionChoices;

/// <summary>
/// What the journal's action filter is offered: each action that occurred once, however often it
/// occurred, named in the request's language — and an action nobody has a name for, as itself.
/// </summary>
public sealed class ListAuditActionChoicesQueryHandlerTests : IDisposable
{
    /// <summary>The instant the seeded entries are recorded at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory journal the handler reads.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Each action that occurred is offered once, named, with the machine action beside the name.</summary>
    [Fact]
    public async Task Each_action_that_occurred_is_offered_once_and_named()
    {
        await WriteAsync(BackupAuditActions.BackupRestored);
        await WriteAsync(BackupAuditActions.BackupRestored);
        await WriteAsync("SomeMarketplaceModuleAction");

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");

            var result = await Handler()
                .HandleAsync(new ListAuditActionChoicesQuery(), CancellationToken.None);

            Assert.Equal(2, result.Value.Count);
            var restored = Assert.Single(result.Value, choice =>
            {
                return choice.Action == BackupAuditActions.BackupRestored;
            });
            Assert.Equal("Аккаунт восстановлен из резервной копии", restored.ActionName);
            var unnamed = Assert.Single(result.Value, choice =>
            {
                return choice.Action == "SomeMarketplaceModuleAction";
            });
            Assert.Equal("SomeMarketplaceModuleAction", unnamed.ActionName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>An empty journal offers no action rather than failing.</summary>
    [Fact]
    public async Task An_empty_journal_offers_no_action()
    {
        var result = await Handler()
            .HandleAsync(new ListAuditActionChoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    /// <summary>A reseller is offered only the actions of its own accounts and its own login — and the administrator, the rest.</summary>
    [Fact]
    public async Task A_reseller_is_offered_only_the_actions_of_its_own_part_of_the_journal()
    {
        var loginId = Guid.NewGuid();
        var ownAccount = Guid.NewGuid();
        var directory = new StubResellerDirectory();
        directory.AddOwnedAccount(ownAccount);
        await WriteAsync("OwnAccountAction", accountId: ownAccount);
        await WriteAsync("OwnLoginAction", actorUserId: loginId);
        await WriteAsync("ForeignAction", accountId: Guid.NewGuid());
        await WriteAsync("ServerWideAction");

        var reseller = await new ListAuditActionChoicesQueryHandler(
                new AuditEventQuery(_context),
                new AuditEventScopeReader(new StubResellerUser(loginId, "reseller", Guid.NewGuid()), directory),
                IdentityTestContext.ActionNames())
            .HandleAsync(new ListAuditActionChoicesQuery(), CancellationToken.None);
        var administrator = await Handler().HandleAsync(new ListAuditActionChoicesQuery(), CancellationToken.None);

        string[] expected = ["OwnAccountAction", "OwnLoginAction"];
        Assert.Equivalent(
            expected,
            reseller.Value.Select(choice =>
            {
                return choice.Action;
            }));
        Assert.Equal(4, administrator.Value.Count);
    }

    /// <summary>Creates the handler under test over the in-memory journal, read as an administrator.</summary>
    /// <returns>The handler.</returns>
    private ListAuditActionChoicesQueryHandler Handler()
    {
        return new ListAuditActionChoicesQueryHandler(
            new AuditEventQuery(_context),
            new AuditEventScopeReader(new StubCurrentUser(), new StubResellerDirectory()),
            IdentityTestContext.ActionNames());
    }

    /// <summary>Records one entry with the given action.</summary>
    /// <param name="action">What was done.</param>
    /// <param name="accountId">The account the entry is stamped with, or <c>null</c> for none.</param>
    /// <param name="actorUserId">The user who did it, or <c>null</c> for none.</param>
    /// <returns>Resolves once the entry is saved.</returns>
    private async Task WriteAsync(string action, Guid? accountId = null, Guid? actorUserId = null)
    {
        _context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), Now, actorUserId, "admin", action, "subject", "203.0.113.7", "agent", true, null, null, accountId));
        await _context.SaveChangesAsync();
    }
}
