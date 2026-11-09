using System.Globalization;
using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Queries.ListMailboxes;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Queries.ListMailboxes;

/// <summary>
/// Listing mailboxes: whose rows are in the answer, the derived status and its filter, the domain filter,
/// usage, owner names, paging and sorting. The ILIKE search is a PostgreSQL function the in-memory provider
/// cannot evaluate, so it is exercised by the HTTP-level suite and not here.
/// </summary>
public sealed class ListMailboxesQueryHandlerTests
{
    /// <summary>The account whose mailboxes the caller in these tests owns.</summary>
    private static readonly Guid CustomerA = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose mailboxes must not appear in a customer's answer.</summary>
    private static readonly Guid CustomerB = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The instant the first seeded mailbox was created; later ones follow a day apart.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A customer sees their own mailboxes and no neighbour's; the owning control finds the neighbour's.</summary>
    [Fact]
    public async Task The_listing_carries_the_callers_own_mailboxes_and_no_neighbours()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var neighbour = new MailTestContext(new CustomerCurrentUser(CustomerB), databaseName);
        neighbour.Accounts.Add(CustomerB, "beta");
        await SeedAsync(neighbour, await neighbour.SeedDomainAsync(CustomerB, "beta.example"), "secret", 512, 0);

        using var context = new MailTestContext(new CustomerCurrentUser(CustomerA), databaseName);
        context.Accounts.Add(CustomerA, "acme");
        await SeedAsync(context, await context.SeedDomainAsync(CustomerA, "acme.example"), "info", 2048, 0);

        var result = await MailboxGroupHandlers.List(context).HandleAsync(
            new ListMailboxesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal("info@acme.example", row.Address);
        Assert.Equal("acme", row.OwnerName);
        Assert.Equal(CustomerA, row.AccountId);
        Assert.Equal(2048, row.QuotaMib);
        Assert.Equal(100, row.SendLimitPerHour);
        Assert.Equal(MailboxStatus.Active, row.Status);
        Assert.Equal("MailboxStatusActive", row.StatusName);
        Assert.Equal(1, result.Value.Total);

        var neighbourRows = await MailboxGroupHandlers.List(neighbour).HandleAsync(
            new ListMailboxesQuery(), CancellationToken.None);
        Assert.Equal("secret@beta.example", Assert.Single(neighbourRows.Value.Items).Address);
    }

    /// <summary>A customer naming another account in the filter gets an empty page, never that account's rows.</summary>
    [Fact]
    public async Task A_customer_naming_another_account_gets_an_empty_page()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var neighbour = new MailTestContext(new CustomerCurrentUser(CustomerB), databaseName);
        neighbour.Accounts.Add(CustomerB, "beta");
        await SeedAsync(neighbour, await neighbour.SeedDomainAsync(CustomerB, "beta.example"), "secret", 512, 0);
        using var context = new MailTestContext(new CustomerCurrentUser(CustomerA), databaseName);
        context.Accounts.Add(CustomerA, "acme");

        var result = await MailboxGroupHandlers.List(context).HandleAsync(
            new ListMailboxesQuery(AccountId: CustomerB), CancellationToken.None);

        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>An administrator sees every account's mailboxes and can narrow to one account.</summary>
    [Fact]
    public async Task An_administrator_sees_every_account_and_can_narrow_to_one()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(CustomerA, "acme");
        context.Accounts.Add(CustomerB, "beta");
        await SeedAsync(context, await context.SeedDomainAsync(CustomerA, "acme.example"), "info", 512, 0);
        await SeedAsync(context, await context.SeedDomainAsync(CustomerB, "beta.example"), "info", 512, 1);
        var handler = MailboxGroupHandlers.List(context);

        var everyone = await handler.HandleAsync(new ListMailboxesQuery(), CancellationToken.None);
        var narrowed = await handler.HandleAsync(new ListMailboxesQuery(AccountId: CustomerB), CancellationToken.None);

        Assert.Equal(2, everyone.Value.Total);
        Assert.Equal("beta", Assert.Single(narrowed.Value.Items).OwnerName);
    }

    /// <summary>The status reads from the owner's switch and the account's state, and the filter keeps each in turn.</summary>
    [Fact]
    public async Task The_status_is_derived_and_its_filter_separates_the_three_readings()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(CustomerA, "acme");
        context.Accounts.Add(CustomerB, "beta", state: AccountState.Suspended);
        var acme = await context.SeedDomainAsync(CustomerA, "acme.example");
        var beta = await context.SeedDomainAsync(CustomerB, "beta.example");
        await SeedAsync(context, acme, "on", 512, 0);
        var off = await SeedAsync(context, acme, "off", 512, 1);
        off.Disable();
        await SeedAsync(context, beta, "held", 512, 2);
        var heldOff = await SeedAsync(context, beta, "heldoff", 512, 3);
        heldOff.Disable();
        await context.Database.SaveChangesAsync(CancellationToken.None);
        var handler = MailboxGroupHandlers.List(context);

        var all = await handler.HandleAsync(new ListMailboxesQuery(Sort: SortRequest.Parse("address")), CancellationToken.None);
        var active = await handler.HandleAsync(new ListMailboxesQuery(Status: MailboxStatus.Active), CancellationToken.None);
        var disabled = await handler.HandleAsync(new ListMailboxesQuery(Status: MailboxStatus.Disabled), CancellationToken.None);
        var suspended = await handler.HandleAsync(new ListMailboxesQuery(Status: MailboxStatus.Suspended), CancellationToken.None);

        Assert.Equal(
            ["held@beta.example:Suspended",
                "heldoff@beta.example:Suspended",
                "off@acme.example:Disabled",
                "on@acme.example:Active",],
            all.Value.Items.Select(row =>
            {
                return row.Address + ":" + row.Status;
            }).ToArray());
        Assert.Equal("on@acme.example", Assert.Single(active.Value.Items).Address);
        Assert.Equal("off@acme.example", Assert.Single(disabled.Value.Items).Address);
        Assert.Equal(
            ["held@beta.example", "heldoff@beta.example"],
            suspended.Value.Items.OrderBy(row =>
            {
                return row.Address;
            }, StringComparer.Ordinal).Select(row =>
            {
                return row.Address;
            }).ToArray());
        Assert.Equal(2, suspended.Value.Total);
        Assert.Equal("MailboxStatusSuspended", suspended.Value.Items[0].StatusName);
    }

    /// <summary>The domain filter matches the exact domain NAME, not a part of it and not an identifier.</summary>
    [Fact]
    public async Task The_domain_filter_matches_the_exact_domain_name()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(CustomerA, "acme");
        var com = await context.SeedDomainAsync(CustomerA, "example.com");
        var org = await context.SeedDomainAsync(CustomerA, "example.org");
        await SeedAsync(context, com, "info", 512, 0);
        await SeedAsync(context, org, "info", 512, 1);
        var handler = MailboxGroupHandlers.List(context);

        var exact = await handler.HandleAsync(
            new ListMailboxesQuery(Domain: FilterValue.Of("Example.COM")), CancellationToken.None);
        var partial = await handler.HandleAsync(
            new ListMailboxesQuery(Domain: FilterValue.Of("example")), CancellationToken.None);
        var byId = await handler.HandleAsync(
            new ListMailboxesQuery(Domain: FilterValue.Of(com.Id.ToString())), CancellationToken.None);

        Assert.Equal("info@example.com", Assert.Single(exact.Value.Items).Address);
        Assert.Empty(partial.Value.Items);
        Assert.Empty(byId.Value.Items);
    }

    /// <summary>The list sorts by address, quota and creation time in both directions, oldest first by default.</summary>
    [Fact]
    public async Task The_list_sorts_by_address_quota_and_creation_time()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(CustomerA, "acme");
        var domain = await context.SeedDomainAsync(CustomerA, "example.com");
        await SeedAsync(context, domain, "bravo", 300, 0);
        await SeedAsync(context, domain, "alpha", 100, 2);
        await SeedAsync(context, domain, "charlie", 200, 1);
        var handler = MailboxGroupHandlers.List(context);

        Assert.Equal(
            ["bravo", "charlie", "alpha"],
            await LocalPartsAsync(handler, new ListMailboxesQuery()));
        Assert.Equal(
            ["alpha", "bravo", "charlie"],
            await LocalPartsAsync(handler, new ListMailboxesQuery(Sort: SortRequest.Parse("address"))));
        Assert.Equal(
            ["charlie", "bravo", "alpha"],
            await LocalPartsAsync(handler, new ListMailboxesQuery(Sort: SortRequest.Parse("-address"))));
        Assert.Equal(
            ["alpha", "charlie", "bravo"],
            await LocalPartsAsync(handler, new ListMailboxesQuery(Sort: SortRequest.Parse("quotaMib"))));
        Assert.Equal(
            ["alpha", "charlie", "bravo"],
            await LocalPartsAsync(handler, new ListMailboxesQuery(Sort: SortRequest.Parse("-createdAt"))));
    }

    /// <summary>Paging answers the requested slice with the true total, and a page past the end is empty.</summary>
    [Fact]
    public async Task Paging_answers_the_requested_slice_with_the_true_total()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(CustomerA, "acme");
        var domain = await context.SeedDomainAsync(CustomerA, "example.com");
        for (var index = 0; index < 12; index++)
        {
            var local = "box" + index.ToString("00", CultureInfo.InvariantCulture);
            await SeedAsync(context, domain, local, 100 + index, index);
        }

        var handler = MailboxGroupHandlers.List(context);
        var second = await handler.HandleAsync(
            new ListMailboxesQuery(Page: 2, PageSize: 10, Sort: SortRequest.Parse("address")), CancellationToken.None);
        var past = await handler.HandleAsync(
            new ListMailboxesQuery(Page: 9, PageSize: 10), CancellationToken.None);

        Assert.Equal(["box10", "box11"], second.Value.Items.Select(row =>
        {
            return row.Address.Split('@')[0];
        }).ToArray());
        Assert.Equal(12, second.Value.Total);
        Assert.Empty(past.Value.Items);
        Assert.Equal(12, past.Value.Total);
    }

    /// <summary>Usage is read once per account on the page, rounded up to whole MiB, and blank where the host gave none.</summary>
    [Fact]
    public async Task Usage_is_read_once_per_account_rounded_up_and_blank_without_a_reading()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(CustomerA, "acme");
        var domain = await context.SeedDomainAsync(CustomerA, "example.com");
        await SeedAsync(context, domain, "exact", 512, 0);
        await SeedAsync(context, domain, "round", 512, 1);
        await SeedAsync(context, domain, "blank", 512, 2);
        await SeedAsync(context, domain, "absent", 512, 3);
        context.Agent.Usage.Add(new AgentMailboxUsage("example.com", "exact", new AgentMailboxUsageReading(3 * 1_048_576UL, 1)));
        context.Agent.Usage.Add(new AgentMailboxUsage("example.com", "round", new AgentMailboxUsageReading(1_048_577UL, 1)));
        context.Agent.Usage.Add(new AgentMailboxUsage("example.com", "blank", null));

        var result = await MailboxGroupHandlers.List(context).HandleAsync(
            new ListMailboxesQuery(), CancellationToken.None);

        var usedBy = result.Value.Items.ToDictionary(
            row =>
            {
                return row.Address.Split('@')[0];
            },
            row =>
            {
                return row.UsedMib;
            });
        Assert.Equal(3L, usedBy["exact"]);
        Assert.Equal(2L, usedBy["round"]);
        Assert.Null(usedBy["blank"]);
        Assert.Null(usedBy["absent"]);
        Assert.Equal(["ListMailboxUsage(acme)"], context.Agent.Calls);
    }

    /// <summary>A host that cannot report usage leaves the column blank and the list intact.</summary>
    [Fact]
    public async Task A_host_that_cannot_report_usage_leaves_the_column_blank_and_the_list_intact()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(CustomerA, "acme");
        await SeedAsync(context, await context.SeedDomainAsync(CustomerA, "example.com"), "info", 512, 0);
        context.Agent.Usage.Add(new AgentMailboxUsage("example.com", "info", new AgentMailboxUsageReading(5_242_880UL, 1)));
        context.Agent.FailNext("ListMailboxUsage", Error.Of(MailboxGroupErrorCodes.AgentUnavailable, ErrorType.Failure));

        var result = await MailboxGroupHandlers.List(context).HandleAsync(
            new ListMailboxesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(result.Value.Items).UsedMib);
    }

    /// <summary>Reads the local parts of one page, in the order the handler returned them.</summary>
    /// <param name="handler">The list handler.</param>
    /// <param name="query">The query to run.</param>
    /// <returns>The local parts, in answer order.</returns>
    private static async Task<string[]> LocalPartsAsync(ListMailboxesQueryHandler handler, ListMailboxesQuery query)
    {
        var result = await handler.HandleAsync(query, CancellationToken.None);

        return result.Value.Items.Select(row =>
        {
            return row.Address.Split('@')[0];
        }).ToArray();
    }

    /// <summary>Records a mailbox with a chosen quota, created a given number of days after the epoch.</summary>
    /// <param name="context">The context to record through.</param>
    /// <param name="domain">The domain the mailbox lives in.</param>
    /// <param name="localPart">The local part.</param>
    /// <param name="quotaMib">The quota.</param>
    /// <param name="daysAfterEpoch">How many days after <see cref="Epoch"/> the mailbox was created.</param>
    /// <returns>The recorded mailbox, tracked by the context.</returns>
    private static async Task<Mailbox> SeedAsync(
        MailTestContext context,
        MailDomain domain,
        string localPart,
        int quotaMib,
        int daysAfterEpoch)
    {
        var mailbox = new Mailbox(
            Guid.NewGuid(),
            domain.AccountId,
            domain.Id,
            localPart,
            StubPasswordHasher.Marker + "x",
            quotaMib,
            100,
            Epoch.AddDays(daysAfterEpoch));
        context.Database.Mailboxes.Add(mailbox);
        await context.Database.SaveChangesAsync(CancellationToken.None);

        return mailbox;
    }
}
