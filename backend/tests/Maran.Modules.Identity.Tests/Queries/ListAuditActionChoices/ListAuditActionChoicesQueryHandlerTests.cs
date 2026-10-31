using System.Globalization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ListAuditActionChoices;
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
        await WriteAsync(AuditActions.BackupRestored);
        await WriteAsync(AuditActions.BackupRestored);
        await WriteAsync("SomeMarketplaceModuleAction");

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");

            var result = await new ListAuditActionChoicesQueryHandler(_context, IdentityTestContext.ActionNames())
                .HandleAsync(new ListAuditActionChoicesQuery(), CancellationToken.None);

            Assert.Equal(2, result.Value.Count);
            var restored = Assert.Single(result.Value, choice =>
            {
                return choice.Action == AuditActions.BackupRestored;
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
        var result = await new ListAuditActionChoicesQueryHandler(_context, IdentityTestContext.ActionNames())
            .HandleAsync(new ListAuditActionChoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    /// <summary>Records one entry with the given action.</summary>
    /// <param name="action">What was done.</param>
    /// <returns>Resolves once the entry is saved.</returns>
    private async Task WriteAsync(string action)
    {
        _context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), Now, null, "admin", action, "subject", "203.0.113.7", "agent", true, null));
        await _context.SaveChangesAsync();
    }
}
