using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Queries.ListMailDomains;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Tests.Queries.ListMailDomains;

/// <summary>
/// Listing mail domains: whose rows are in the answer, the derived status and its filter, mailbox
/// counts, owner names, paging and sorting. The ILIKE search is a PostgreSQL function the in-memory
/// provider cannot evaluate, so it is exercised by the HTTP-level suite and not here.
/// </summary>
public sealed class ListMailDomainsQueryHandlerTests
{
    /// <summary>The account whose domains the caller in these tests owns.</summary>
    private static readonly Guid CustomerA = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose domains must not appear in a customer's answer.</summary>
    private static readonly Guid CustomerB = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>A customer sees their own domains and no neighbour's; the owning control finds the neighbour's.</summary>
    [Fact]
    public async Task The_listing_carries_the_callers_own_domains_and_no_neighbours()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var neighbour = new MailTestContext(new CustomerCurrentUser(CustomerB), databaseName);
        neighbour.Accounts.Add(CustomerB, "beta");
        await neighbour.SeedDomainAsync(CustomerB, "beta.example");

        using var context = new MailTestContext(new CustomerCurrentUser(CustomerA), databaseName);
        context.Accounts.Add(CustomerA, "acme");
        await context.SeedDomainAsync(CustomerA, "acme.example");

        var result = await DomainGroupHandlers.List(context).HandleAsync(
            new ListMailDomainsQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value.Items);
        Assert.Equal("acme.example", row.Name);
        Assert.Equal("acme", row.OwnerName);
        Assert.Equal("m20261107-1", row.DkimSelector);
        Assert.Equal(MailDomainStatus.Active, row.Status);
        Assert.Equal("MailDomainStatusActive", row.StatusName);

        var neighbourRows = await DomainGroupHandlers.List(neighbour).HandleAsync(
            new ListMailDomainsQuery(), CancellationToken.None);
        Assert.Equal("beta.example", Assert.Single(neighbourRows.Value.Items).Name);
    }

    /// <summary>An administrator sees every account's domains and can narrow to one account.</summary>
    [Fact]
    public async Task An_administrator_sees_every_account_and_can_narrow_to_one()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(CustomerA, "acme");
        context.Accounts.Add(CustomerB, "beta");
        await context.SeedDomainAsync(CustomerA, "acme.example");
        await context.SeedDomainAsync(CustomerB, "beta.example");
        var handler = DomainGroupHandlers.List(context);

        var everyone = await handler.HandleAsync(new ListMailDomainsQuery(), CancellationToken.None);
        var narrowed = await handler.HandleAsync(new ListMailDomainsQuery(AccountId: CustomerB), CancellationToken.None);

        Assert.Equal(2, everyone.Value.Total);
        Assert.Equal("beta", Assert.Single(narrowed.Value.Items).OwnerName);
    }

    /// <summary>The status filter keeps suspended accounts' domains or the others, and each is the other's control.</summary>
    [Fact]
    public async Task The_status_filter_separates_suspended_accounts_from_active_ones()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(CustomerA, "acme");
        context.Accounts.Add(CustomerB, "beta", state: AccountState.Suspended);
        await context.SeedDomainAsync(CustomerA, "acme.example");
        await context.SeedDomainAsync(CustomerB, "beta.example");
        var handler = DomainGroupHandlers.List(context);

        var suspended = await handler.HandleAsync(
            new ListMailDomainsQuery(Status: MailDomainStatus.Suspended), CancellationToken.None);
        var active = await handler.HandleAsync(
            new ListMailDomainsQuery(Status: MailDomainStatus.Active), CancellationToken.None);
        var all = await handler.HandleAsync(new ListMailDomainsQuery(), CancellationToken.None);

        var suspendedRow = Assert.Single(suspended.Value.Items);
        Assert.Equal("beta.example", suspendedRow.Name);
        Assert.Equal(MailDomainStatus.Suspended, suspendedRow.Status);
        Assert.Equal("acme.example", Assert.Single(active.Value.Items).Name);
        Assert.Equal(2, all.Value.Total);
    }

    /// <summary>The mailbox count of each row is the number of mailboxes of that domain only.</summary>
    [Fact]
    public async Task Each_row_counts_the_mailboxes_of_its_own_domain()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(CustomerA, "acme");
        var first = await context.SeedDomainAsync(CustomerA, "a.example");
        var second = await context.SeedDomainAsync(CustomerA, "b.example");
        await context.SeedDomainAsync(CustomerA, "c.example");
        await context.SeedMailboxAsync(first, "one");
        await context.SeedMailboxAsync(first, "two");
        await context.SeedMailboxAsync(second, "three");

        var result = await DomainGroupHandlers.List(context).HandleAsync(
            new ListMailDomainsQuery(Sort: SortRequest.Parse("name")), CancellationToken.None);

        Assert.Equal([2, 1, 0], result.Value.Items.Select(row =>
        {
            return row.MailboxCount;
        }).ToArray());
    }

    /// <summary>Paging reports the true total and the page asked for; sorting by name flips with the direction.</summary>
    [Fact]
    public async Task Paging_and_sorting_follow_the_request()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(CustomerA, "acme");
        foreach (var name in new[] { "a.example", "b.example", "c.example" })
        {
            await context.SeedDomainAsync(CustomerA, name);
        }

        var handler = DomainGroupHandlers.List(context);
        var secondPage = await handler.HandleAsync(
            new ListMailDomainsQuery(Page: 2, PageSize: 10, Sort: SortRequest.Parse("name")), CancellationToken.None);
        var descending = await handler.HandleAsync(
            new ListMailDomainsQuery(Sort: SortRequest.Parse("-name")), CancellationToken.None);

        Assert.Empty(secondPage.Value.Items);
        Assert.Equal(3, secondPage.Value.Total);
        Assert.Equal(2, secondPage.Value.Page);
        Assert.Equal(
            ["c.example", "b.example", "a.example"],
            descending.Value.Items.Select(row =>
            {
                return row.Name;
            }).ToArray());
    }
}
