using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Queries.ListMailAliases;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Tests.Queries.ListMailAliases;

/// <summary>
/// Listing addresses: whose rows are in the answer, the paging, sorting and filters, and the names the
/// backend supplies. The text search is an <c>ILIKE</c> over PostgreSQL and is exercised against the real
/// database by the host's integration suite; the in-memory provider used here cannot evaluate it.
/// </summary>
public sealed class ListMailAliasesQueryHandlerTests
{
    /// <summary>The account whose addresses the caller in these tests owns.</summary>
    private static readonly Guid AccountA = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose addresses must not appear in a customer's answer.</summary>
    private static readonly Guid AccountB = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The instant the first seeded address is created at; later ones are a day apart.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A row carries its address, kind, localized kind name, owner name and targets in the order given.</summary>
    [Fact]
    public async Task A_row_carries_the_address_the_kind_name_the_owner_and_the_targets_in_order()
    {
        using var context = Customer(AccountA, Guid.NewGuid().ToString());
        context.Accounts.Add(AccountA, "acme");
        var domain = await context.SeedDomainAsync(AccountA, "example.com");
        var alias = await MailAliasSeeds.SeedAliasAsync(
            context, domain, "info", MailAliasKind.Alias, "z@example.com", "a@example.com", "m@example.com");

        var result = await MailAliasHandlers.List(context).HandleAsync(
            new ListMailAliasesQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value.Items);
        Assert.Equal(alias.Id, row.Id);
        Assert.Equal(AccountA, row.AccountId);
        Assert.Equal("acme", row.OwnerName);
        Assert.Equal(domain.Id, row.MailDomainId);
        Assert.Equal("info@example.com", row.Address);
        Assert.Equal(MailAliasKind.Alias, row.Kind);
        Assert.Equal("MailAliasKindAlias", row.KindName);
        Assert.Equal(["z@example.com", "a@example.com", "m@example.com"], row.Targets);
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>A catch-all is shown as the wildcard address with its own kind name.</summary>
    [Fact]
    public async Task A_catch_all_is_shown_as_the_wildcard_address()
    {
        using var context = Customer(AccountA, Guid.NewGuid().ToString());
        context.Accounts.Add(AccountA, "acme");
        var domain = await context.SeedDomainAsync(AccountA, "example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "*", MailAliasKind.CatchAll, "boss@example.com");

        var result = await MailAliasHandlers.List(context).HandleAsync(
            new ListMailAliasesQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value.Items);
        Assert.Equal("*@example.com", row.Address);
        Assert.Equal("MailAliasKindCatchAll", row.KindName);
    }

    /// <summary>The listing carries the caller's own addresses and no neighbour's; a customer naming the neighbour gets nothing.</summary>
    /// <remarks>
    /// The inverse control is the first half of every pair: the caller's own row IS present, so the query is
    /// not merely returning nothing, and asking for one's own account id returns it.
    /// </remarks>
    [Fact]
    public async Task The_listing_carries_the_callers_own_addresses_and_no_neighbours()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var neighbour = Customer(AccountB, databaseName);
        neighbour.Accounts.Add(AccountB, "beta");
        var theirDomain = await neighbour.SeedDomainAsync(AccountB, "beta.example");
        await MailAliasSeeds.SeedAliasAsync(neighbour, theirDomain, "secret", MailAliasKind.Alias, "x@beta.example");

        using var context = Customer(AccountA, databaseName);
        context.Accounts.Add(AccountA, "acme");
        context.Accounts.Add(AccountB, "beta");
        var ownDomain = await context.SeedDomainAsync(AccountA, "example.com");
        await MailAliasSeeds.SeedAliasAsync(context, ownDomain, "info", MailAliasKind.Alias, "bob@example.com");
        var handler = MailAliasHandlers.List(context);

        var all = await handler.HandleAsync(new ListMailAliasesQuery(), CancellationToken.None);
        var naming = await handler.HandleAsync(new ListMailAliasesQuery(AccountId: AccountB), CancellationToken.None);
        var own = await handler.HandleAsync(new ListMailAliasesQuery(AccountId: AccountA), CancellationToken.None);

        Assert.Equal("info@example.com", Assert.Single(all.Value.Items).Address);
        Assert.Equal(1, all.Value.Total);
        Assert.Empty(naming.Value.Items);
        Assert.Equal(0, naming.Value.Total);
        Assert.Equal("info@example.com", Assert.Single(own.Value.Items).Address);
    }

    /// <summary>An administrator sees every account's addresses with each owner's name, and can narrow to one account.</summary>
    [Fact]
    public async Task An_administrator_sees_every_account_and_can_narrow_to_one()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(AccountA, "acme");
        context.Accounts.Add(AccountB, "beta");
        var domainA = await context.SeedDomainAsync(AccountA, "example.com");
        var domainB = await context.SeedDomainAsync(AccountB, "beta.example");
        await MailAliasSeeds.SeedAliasAsync(context, domainA, "info", MailAliasKind.Alias, "bob@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domainB, "info", MailAliasKind.Alias, "bob@beta.example");
        var handler = MailAliasHandlers.List(context);

        var all = await handler.HandleAsync(
            new ListMailAliasesQuery(Sort: SortRequest.Parse("address")), CancellationToken.None);
        var onlyB = await handler.HandleAsync(new ListMailAliasesQuery(AccountId: AccountB), CancellationToken.None);

        Assert.Equal(["info@beta.example", "info@example.com"], Addresses(all.Value.Items));
        Assert.Equal(["beta", "acme"], OwnerNames(all.Value.Items));
        Assert.Equal(["info@beta.example"], Addresses(onlyB.Value.Items));
    }

    /// <summary>The domain filter matches the domain by name, whatever case it is typed in.</summary>
    [Fact]
    public async Task The_domain_filter_matches_the_domain_by_name_in_any_case()
    {
        using var context = Customer(AccountA, Guid.NewGuid().ToString());
        context.Accounts.Add(AccountA, "acme");
        var first = await context.SeedDomainAsync(AccountA, "example.com");
        var second = await context.SeedDomainAsync(AccountA, "example.net");
        await MailAliasSeeds.SeedAliasAsync(context, first, "one", MailAliasKind.Alias, "x@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, second, "two", MailAliasKind.Alias, "x@example.net");
        var handler = MailAliasHandlers.List(context);

        var narrowed = await handler.HandleAsync(
            new ListMailAliasesQuery(Domain: FilterValue.Of("Example.NET")), CancellationToken.None);
        var none = await handler.HandleAsync(
            new ListMailAliasesQuery(Domain: FilterValue.Of("example.org")), CancellationToken.None);
        var everything = await handler.HandleAsync(new ListMailAliasesQuery(), CancellationToken.None);

        Assert.Equal(["two@example.net"], Addresses(narrowed.Value.Items));
        Assert.Empty(none.Value.Items);
        Assert.Equal(2, everything.Value.Total);
    }

    /// <summary>The kind filter returns only that kind.</summary>
    [Fact]
    public async Task The_kind_filter_returns_only_that_kind()
    {
        using var context = Customer(AccountA, Guid.NewGuid().ToString());
        context.Accounts.Add(AccountA, "acme");
        var domain = await context.SeedDomainAsync(AccountA, "example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "inner", MailAliasKind.Alias, "x@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "outer", MailAliasKind.Forwarder, "me@example.org");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "*", MailAliasKind.CatchAll, "boss@example.com");
        var handler = MailAliasHandlers.List(context);

        var forwarders = await handler.HandleAsync(
            new ListMailAliasesQuery(Kind: MailAliasKind.Forwarder), CancellationToken.None);
        var catchAlls = await handler.HandleAsync(
            new ListMailAliasesQuery(Kind: MailAliasKind.CatchAll), CancellationToken.None);

        Assert.Equal(["outer@example.com"], Addresses(forwarders.Value.Items));
        Assert.Equal(["*@example.com"], Addresses(catchAlls.Value.Items));
    }

    /// <summary>A page holds only its slice, and carries the true total across every page.</summary>
    [Fact]
    public async Task A_page_holds_only_its_slice_and_carries_the_total()
    {
        using var context = Customer(AccountA, Guid.NewGuid().ToString());
        context.Accounts.Add(AccountA, "acme");
        var domain = await context.SeedDomainAsync(AccountA, "example.com");
        for (var index = 0; index < 12; index++)
        {
            await MailAliasSeeds.SeedAliasAsync(
                context,
                domain,
                $"a{index:00}",
                MailAliasKind.Alias,
                Epoch.AddDays(index),
                "x@example.com");
        }

        var handler = MailAliasHandlers.List(context);

        var first = await handler.HandleAsync(new ListMailAliasesQuery(Page: 1, PageSize: 10), CancellationToken.None);
        var second = await handler.HandleAsync(new ListMailAliasesQuery(Page: 2, PageSize: 10), CancellationToken.None);
        var beyond = await handler.HandleAsync(new ListMailAliasesQuery(Page: 5, PageSize: 10), CancellationToken.None);

        Assert.Equal(10, first.Value.Items.Count);
        Assert.Equal(12, first.Value.Total);
        Assert.Equal(["a10@example.com", "a11@example.com"], Addresses(second.Value.Items));
        Assert.Empty(beyond.Value.Items);
        Assert.Equal(12, beyond.Value.Total);
    }

    /// <summary>The default order is oldest first, and the address sort orders by the whole address in either direction.</summary>
    [Fact]
    public async Task The_default_order_is_oldest_first_and_the_address_sort_orders_by_the_whole_address()
    {
        using var context = Customer(AccountA, Guid.NewGuid().ToString());
        context.Accounts.Add(AccountA, "acme");
        var com = await context.SeedDomainAsync(AccountA, "example.com");
        var net = await context.SeedDomainAsync(AccountA, "example.net");
        await MailAliasSeeds.SeedAliasAsync(context, net, "info", MailAliasKind.Alias, Epoch, "x@example.net");
        await MailAliasSeeds.SeedAliasAsync(context, com, "zed", MailAliasKind.Alias, Epoch.AddDays(1), "x@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, com, "info", MailAliasKind.Alias, Epoch.AddDays(2), "x@example.com");
        var handler = MailAliasHandlers.List(context);

        var oldest = await handler.HandleAsync(new ListMailAliasesQuery(), CancellationToken.None);
        var newest = await handler.HandleAsync(
            new ListMailAliasesQuery(Sort: SortRequest.Parse("-createdAt")), CancellationToken.None);
        var byAddress = await handler.HandleAsync(
            new ListMailAliasesQuery(Sort: SortRequest.Parse("address")), CancellationToken.None);
        var byAddressDescending = await handler.HandleAsync(
            new ListMailAliasesQuery(Sort: SortRequest.Parse("-address")), CancellationToken.None);

        Assert.Equal(["info@example.net", "zed@example.com", "info@example.com"], Addresses(oldest.Value.Items));
        Assert.Equal(["info@example.com", "zed@example.com", "info@example.net"], Addresses(newest.Value.Items));
        Assert.Equal(["info@example.com", "info@example.net", "zed@example.com"], Addresses(byAddress.Value.Items));
        Assert.Equal(
            ["zed@example.com", "info@example.net", "info@example.com"],
            Addresses(byAddressDescending.Value.Items));
    }

    /// <summary>Builds a customer context on a named database.</summary>
    /// <param name="accountId">The account the caller acts for.</param>
    /// <param name="databaseName">The in-memory database to share.</param>
    /// <returns>The assembled module, seen as that customer.</returns>
    private static MailTestContext Customer(Guid accountId, string databaseName)
    {
        return new MailTestContext(new CustomerCurrentUser(accountId), databaseName);
    }

    /// <summary>The addresses of a page, in the order shown.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>Their addresses.</returns>
    private static string[] Addresses(IEnumerable<MailAliasDto> rows)
    {
        return rows
            .Select(row =>
            {
                return row.Address;
            })
            .ToArray();
    }

    /// <summary>The owner names of a page, in the order shown.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>Their owner names.</returns>
    private static string[] OwnerNames(IEnumerable<MailAliasDto> rows)
    {
        return rows
            .Select(row =>
            {
                return row.OwnerName;
            })
            .ToArray();
    }
}
