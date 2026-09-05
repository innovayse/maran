using Maran.Modules.Sftp.IntegrationEvents.Handlers;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sftp.Tests.IntegrationEvents.Handlers;

/// <summary>
/// What the module releases when an account is deleted, what it leaves alone, and what it does with
/// a failure.
/// </summary>
public sealed class AccountDeletingHandlerTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Deleting an account removes every sftp row it owns.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_every_sftp_row_it_owns()
    {
        // Two rows, because a handler that removed only the first would pass a single-row test and
        // leave a live credential listed against an account that no longer exists.
        var store = await SeedAsync();
        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), store);

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        Assert.Empty(await context.SftpUsers.IgnoreQueryFilters()
            .Where(row => row.AccountId == OwnerAccountId).ToListAsync());
    }

    /// <summary>Deleting an account leaves another tenants rows alone.</summary>
    [Fact]
    public async Task Deleting_an_account_leaves_another_tenants_rows_alone()
    {
        // The filter is bypassed on purpose in this handler, so the `Where` clause is the ONLY
        // thing keeping the cascade to one account. A test that seeded one tenant could not tell a
        // scoped delete from a truncate.
        var store = await SeedAsync();
        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), store);

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);

        Assert.Equal(
            ["backup"],
            await context.SftpUsers.IgnoreQueryFilters().Select(row => row.Name).ToListAsync());
    }

    /// <summary>An account with no sftp rows is released without a save.</summary>
    [Fact]
    public async Task An_account_with_no_sftp_rows_is_released_without_a_save()
    {
        // The context is built to throw on save, so reaching one at all fails this. An account
        // deletion must not be turned into a failure by a module that had nothing to release.
        using var context = SftpTestContext.Create(
            FakeCurrentUser.Admin(),
            saveFailure: new InvalidOperationException("nothing should have been saved"));

        await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);
    }

    /// <summary>A save that fails propagates so the deletion can be abandoned.</summary>
    [Fact]
    public async Task A_save_that_fails_propagates_so_the_deletion_can_be_abandoned()
    {
        // Swallowed here, the account would go and these rows would stay — listed in the panel
        // against an account that no longer exists, and inherited by whoever is given that system
        // user name next.
        var store = await SeedAsync();
        using var context = SftpTestContext.Create(
            FakeCurrentUser.Admin(),
            store,
            new InvalidOperationException("the schema is not there"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountDeletingHandler(context).HandleAsync(Deleting(OwnerAccountId), CancellationToken.None);
        });
    }

    /// <summary>The event for <paramref name="accountId"/>, as the Accounts module sends it.</summary>
    /// <param name="accountId">The account being removed.</param>
    private static AccountDeleting Deleting(Guid accountId)
    {
        return new AccountDeleting(accountId, "owner");
    }

    /// <summary>Seeds one shared store with two tenants' logins and returns its name.</summary>
    /// <returns>The in-memory database both contexts open.</returns>
    private static async Task<string> SeedAsync()
    {
        var store = Guid.NewGuid().ToString();

        using var seed = SftpTestContext.Create(FakeCurrentUser.Admin(), store);
        seed.SftpUsers.AddRange(
            SftpTestContext.Row(OwnerAccountId, "owner", "deploy"),
            SftpTestContext.Row(OwnerAccountId, "owner", "web"),
            SftpTestContext.Row(StrangerAccountId, "stranger", "backup"));
        await seed.SaveChangesAsync();

        return store;
    }
}
