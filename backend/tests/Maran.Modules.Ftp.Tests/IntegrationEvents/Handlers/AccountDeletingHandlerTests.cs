using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ftp.Tests.IntegrationEvents.Handlers;

/// <summary>What an account deletion removes from this module, and what it deliberately leaves.</summary>
public sealed class AccountDeletingHandlerTests
{
    /// <summary>The account being deleted.</summary>
    private static readonly Guid Deleted = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>An account that is not being deleted, whose rows must survive.</summary>
    private static readonly Guid Surviving = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>Every login row of the deleted account goes, and no other account's does.</summary>
    /// <remarks>
    /// The surviving row is the inverse control: a handler that removed everything would satisfy an
    /// assertion that only counted the deleted account's rows, and would silently erase every other
    /// customer's logins from the panel while leaving them working on the host.
    /// </remarks>
    [Fact]
    public async Task Every_login_row_of_the_deleted_account_goes_and_no_other_accounts_does()
    {
        using var context = new FtpTestContext();
        await SeedAsync(context, Deleted, "acme", "files");
        await SeedAsync(context, Deleted, "acme", "deploy");
        await SeedAsync(context, Surviving, "beta", "files");

        await context.AccountDeletingHandler.HandleAsync(
            new AccountDeleting(Deleted, "acme"), CancellationToken.None);

        var remaining = await context.Database.FtpUsers.ToListAsync(CancellationToken.None);
        Assert.Equal(Surviving, Assert.Single(remaining).AccountId);
    }

    /// <summary>An account with no logins is handled without a write and without a failure.</summary>
    /// <remarks>
    /// Deleting an account must not fail because this module happens to hold nothing for it — the
    /// failure would propagate and abandon the whole deletion.
    /// </remarks>
    [Fact]
    public async Task An_account_with_no_logins_is_handled_without_failing_the_deletion()
    {
        using var context = new FtpTestContext();
        await SeedAsync(context, Surviving, "beta", "files");

        await context.AccountDeletingHandler.HandleAsync(
            new AccountDeleting(Deleted, "acme"), CancellationToken.None);

        Assert.Single(await context.Database.FtpUsers.ToListAsync(CancellationToken.None));
    }

    /// <summary>Writes one login for an account straight into the database.</summary>
    /// <param name="context">The assembled module whose database receives the row.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="username">The account's system user name, which prefixes the login.</param>
    /// <param name="name">The login's suffix.</param>
    private static async Task SeedAsync(FtpTestContext context, Guid accountId, string username, string name)
    {
        context.Database.FtpUsers.Add(
            new FtpUser(Guid.NewGuid(), accountId, name, $"{username}_{name}", context.Clock.UtcNow));
        await context.Database.SaveChangesAsync(CancellationToken.None);
    }
}
