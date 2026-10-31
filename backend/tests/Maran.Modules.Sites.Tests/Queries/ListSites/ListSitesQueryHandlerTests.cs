using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Queries.ListSites;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.Queries.ListSites;

/// <summary>Behavioral contract of <see cref="ListSitesQueryHandler"/>.</summary>
/// <remarks>
/// UNOBSERVED HERE: the search, on the domain and on the owner's name alike. It is an <c>ILIKE</c>,
/// which only PostgreSQL translates, so the InMemory provider cannot run it;
/// <c>SiteListEndpointTests</c> covers both halves against a real server, the owner half through the
/// real Accounts directory.
/// </remarks>
public sealed class ListSitesQueryHandlerTests
{
    /// <summary>The instant every site in these tests is created at unless a test says otherwise.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Listing sites returns only the callers own, and the total counts only those.</summary>
    [Fact]
    public async Task Listing_sites_returns_only_the_callers_own_and_the_total_counts_only_those()
    {
        // No account is named, so the handler's own account clause does nothing — the context's
        // tenant filter is what scopes this, which is the point of putting it there (spec §8).
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, theirs);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var result = await HandleAsync(context, new ListSitesQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(["mine.example.com"], Domains(result.Value.Items));
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>Listing sites as an administrator returns every tenants.</summary>
    [Fact]
    public async Task Listing_sites_as_an_administrator_returns_every_tenants()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, theirs);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var result = await HandleAsync(context, new ListSitesQuery());

        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(2, result.Value.Total);
    }

    /// <summary>With no sort named, sites are listed oldest first.</summary>
    [Fact]
    public async Task With_no_sort_named_sites_are_listed_oldest_first()
    {
        // Inserted out of order so the assertion cannot pass by insertion-order accident.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedSitesAsync(
            database,
            NewSite(account, "second.example.com", Epoch.AddDays(1)),
            NewSite(account, "first.example.com", Epoch),
            NewSite(account, "third.example.com", Epoch.AddDays(2)));
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await HandleAsync(context, new ListSitesQuery());

        Assert.Equal(["first.example.com", "second.example.com", "third.example.com"], Domains(result.Value.Items));
    }

    /// <summary>A descending domain sort orders the page by domain, last first.</summary>
    [Fact]
    public async Task A_descending_domain_sort_orders_the_page_by_domain_last_first()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedSitesAsync(
            database,
            NewSite(account, "bravo.example.com", Epoch),
            NewSite(account, "charlie.example.com", Epoch),
            NewSite(account, "alpha.example.com", Epoch));
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var result = await HandleAsync(context, new ListSitesQuery(Sort: SortRequest.Parse("-domain")));

        Assert.Equal(["charlie.example.com", "bravo.example.com", "alpha.example.com"], Domains(result.Value.Items));
    }

    /// <summary>The second page holds the rows after the first, and the total counts every match.</summary>
    [Fact]
    public async Task The_second_page_holds_the_rows_after_the_first_and_the_total_counts_every_match()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedSitesAsync(database, [.. Enumerable.Range(0, 12).Select(day =>
        {
            return NewSite(account, $"site{day:D2}.example.com", Epoch.AddDays(day));
        })]);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var result = await HandleAsync(context, new ListSitesQuery(Page: 2, PageSize: 10));

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(2, result.Value.Page);
        Assert.Equal(10, result.Value.PageSize);
        Assert.Equal(["site10.example.com", "site11.example.com"], Domains(result.Value.Items));
    }

    /// <summary>A page past the last one answers empty with the true total and the page asked for.</summary>
    [Fact]
    public async Task A_page_past_the_last_one_answers_empty_with_the_true_total_and_the_page_asked_for()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, Guid.NewGuid());
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var result = await HandleAsync(context, new ListSitesQuery(Page: 5, PageSize: 10));

        Assert.Empty(result.Value.Items);
        Assert.Equal(2, result.Value.Total);
        Assert.Equal(5, result.Value.Page);
    }

    /// <summary>An administrator naming one account is listed only that account's sites.</summary>
    /// <remarks>
    /// Seeded with two accounts and run as an administrator, whom no tenant filter narrows, so the
    /// other account's row is visible to the context and only the account filter can remove it.
    /// </remarks>
    [Fact]
    public async Task An_administrator_naming_one_account_is_listed_only_that_accounts_sites()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, theirs);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var result = await HandleAsync(context, new ListSitesQuery(AccountId: theirs));

        Assert.Equal(["theirs.example.com"], Domains(result.Value.Items));
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>A customer naming another account is listed nothing, with a total of zero.</summary>
    /// <remarks>
    /// The account filter is ANDed with the tenant filter, never in place of it: the customer's own
    /// row exists and is not returned either, because it is not the account they named.
    /// </remarks>
    [Fact]
    public async Task A_customer_naming_another_account_is_listed_nothing_with_a_total_of_zero()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, theirs);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var result = await HandleAsync(context, new ListSitesQuery(AccountId: theirs));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>The backend, PHP version and status filters each narrow the list to their own rows.</summary>
    [Fact]
    public async Task The_backend_php_version_and_status_filters_each_narrow_the_list()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var disabled = NewSite(account, "disabled.example.com", Epoch);
        disabled.Disable();
        await SeedSitesAsync(
            database,
            NewSite(account, "php83.example.com", Epoch),
            SitesTestContext.PhpSite(account, "php82.example.com", phpVersion: "8.2"),
            new Site(
                Guid.NewGuid(),
                account,
                "static.example.com",
                [],
                SiteBackendType.Static,
                string.Empty,
                string.Empty,
                "/home/acct/sites/static.example.com",
                Epoch),
            disabled);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var statics = await HandleAsync(context, new ListSitesQuery(BackendType: SiteBackendType.Static));
        var php82 = await HandleAsync(context, new ListSitesQuery(PhpVersion: FilterValue.Of("8.2")));
        var off = await HandleAsync(context, new ListSitesQuery(Status: SiteStatus.Disabled));

        Assert.Equal(["static.example.com"], Domains(statics.Value.Items));
        Assert.Equal(["php82.example.com"], Domains(php82.Value.Items));
        Assert.Equal(["disabled.example.com"], Domains(off.Value.Items));
    }

    /// <summary>Each row carries its owner's name, read for the whole page in one batched lookup.</summary>
    [Fact]
    public async Task Each_row_carries_its_owners_name_read_for_the_whole_page_in_one_lookup()
    {
        var acme = Guid.NewGuid();
        var globex = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedSitesAsync(
            database,
            NewSite(acme, "one.example.com", Epoch),
            NewSite(acme, "two.example.com", Epoch.AddDays(1)),
            NewSite(globex, "three.example.com", Epoch.AddDays(2)));
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var directory = new StubAccountDirectory(Snapshot(acme, "acme"), Snapshot(globex, "globex"));

        var result = await new ListSitesQueryHandler(context, directory)
            .HandleAsync(new ListSitesQuery(), CancellationToken.None);

        Assert.Equal(["acme", "acme", "globex"], result.Value.Items.Select(site =>
        {
            return site.OwnerName;
        }));
        var lookup = Assert.Single(directory.NameLookups);
        Assert.Equal(new[] { acme, globex }.Order(), lookup.Order());
        Assert.Empty(directory.Lookups);
    }

    /// <summary>A row whose owner the directory does not answer for carries an empty owner name.</summary>
    /// <remarks>
    /// The directory answers nothing for an account the caller may not see, exactly as for one that
    /// does not exist; the row is still the caller's to see, so it is listed, unlabelled.
    /// </remarks>
    [Fact]
    public async Task A_row_whose_owner_the_directory_does_not_answer_for_carries_an_empty_owner_name()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedSitesAsync(database, NewSite(account, "orphan.example.com", Epoch));
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var result = await new ListSitesQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSitesQuery(), CancellationToken.None);

        Assert.Equal(string.Empty, Assert.Single(result.Value.Items).OwnerName);
    }

    /// <summary>An empty page names no account to the directory and runs no owner search.</summary>
    [Fact]
    public async Task An_empty_page_names_no_account_to_the_directory_and_runs_no_owner_search()
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());
        var directory = new StubAccountDirectory();

        var result = await new ListSitesQueryHandler(context, directory)
            .HandleAsync(new ListSitesQuery(), CancellationToken.None);

        Assert.Empty(result.Value.Items);
        Assert.Empty(Assert.Single(directory.NameLookups));
        Assert.Empty(directory.NameSearches);
    }

    /// <summary>Runs the handler with a directory that knows no account.</summary>
    /// <param name="context">The context to read through.</param>
    /// <param name="query">The list request.</param>
    private static Task<Result<PagedResult<SiteDto>>> HandleAsync(SitesDbContext context, ListSitesQuery query)
    {
        return new ListSitesQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(query, CancellationToken.None);
    }

    /// <summary>The domains of a page, in page order.</summary>
    /// <param name="items">The page's rows.</param>
    private static List<string> Domains(IReadOnlyList<SiteDto> items)
    {
        return items
            .Select(site =>
            {
                return site.Domain;
            })
            .ToList();
    }

    /// <summary>Builds a PHP-backed site created at <paramref name="createdAt"/>.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site primary domain.</param>
    /// <param name="createdAt">The creation instant.</param>
    private static Site NewSite(Guid accountId, string domain, DateTimeOffset createdAt)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            [],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            "/home/acct/sites/" + domain,
            createdAt);
    }

    /// <summary>A snapshot carrying only the name the list reads.</summary>
    /// <param name="accountId">The account's identity.</param>
    /// <param name="name">The account's name.</param>
    private static AccountSnapshot Snapshot(Guid accountId, string name)
    {
        return new AccountSnapshot(accountId, name, 5, 5, 5, 5, 3, 1024, AccountState.Active);
    }

    /// <summary>Writes the given sites into one database, as an administrator.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="sites">The rows to write.</param>
    private static async Task SeedSitesAsync(string database, params Site[] sites)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        seed.Sites.AddRange(sites);
        await seed.SaveChangesAsync();
    }

    /// <summary>Writes one site for each of two accounts into one database.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="mine">The account the test's customer owns.</param>
    /// <param name="theirs">The other tenant's account.</param>
    private static Task SeedAsync(string database, Guid mine, Guid theirs)
    {
        return SeedSitesAsync(
            database,
            SitesTestContext.PhpSite(mine, "mine.example.com"),
            SitesTestContext.PhpSite(theirs, "theirs.example.com"));
    }
}
