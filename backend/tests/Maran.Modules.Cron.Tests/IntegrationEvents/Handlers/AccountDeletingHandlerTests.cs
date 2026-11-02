using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.IntegrationEvents.Handlers;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Cron.Tests.IntegrationEvents.Handlers;

/// <summary>What a deleted account leaves in the module's table: nothing.</summary>
public sealed class AccountDeletingHandlerTests
{
    /// <summary>Every run of the deleted account goes, whoever asked, and other accounts' runs stay.</summary>
    [Fact]
    public async Task Every_run_of_the_deleted_account_goes_and_other_accounts_runs_stay()
    {
        var deleted = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var name = Guid.NewGuid().ToString();
        var seed = InMemoryCronDatabase.Open(name, FakeCurrentUser.Admin());
        seed.CronRuns.AddRange(Run(deleted), Run(deleted), Run(kept));
        await seed.SaveChangesAsync();

        // A caller who is not the account's owner, as an administrator deleting a customer is not.
        var handler = new AccountDeletingHandler(InMemoryCronDatabase.Open(name, FakeCurrentUser.Customer(kept)));
        await handler.HandleAsync(new AccountDeleting(deleted, "alice"), default);

        var left = await InMemoryCronDatabase.Open(name, FakeCurrentUser.Admin()).CronRuns.ToListAsync();
        Assert.Equal(kept, Assert.Single(left).AccountId);
    }

    /// <summary>A run of <paramref name="accountId"/>.</summary>
    /// <param name="accountId">The owner.</param>
    /// <returns>The run.</returns>
    private static CronRun Run(Guid accountId)
    {
        return new CronRun(Guid.NewGuid(), accountId, "e", DateTimeOffset.UnixEpoch, 1, 0, null, "", "");
    }
}
