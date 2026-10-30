using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.ListPlans;
using Maran.Modules.Accounts.Resources;
using Maran.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Tests.Queries.ListPlans;

/// <summary>
/// Behavioral contract of <see cref="ListPlansQueryHandler"/>, run against a real
/// <see cref="AccountsDbContext"/> backed by the EF Core InMemory provider, each test getting its
/// own uniquely-named database (rules/testing.md "Determinism"), with a fake
/// <see cref="IStringLocalizer{T}"/> standing in for the resx-backed one so display-name
/// resolution is exercised without depending on the real resource files.
/// </summary>
public sealed class ListPlansQueryHandlerTests
{
    /// <summary>Builds a fresh, isolated in-memory <see cref="AccountsDbContext"/>.</summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Empty store returns an empty page with a total of zero.</summary>
    [Fact]
    public async Task Empty_store_returns_an_empty_page_with_a_total_of_zero()
    {
        await using var dbContext = CreateDbContext();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(new ListPlansQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>Returns every plan the store holds with its display name resolved.</summary>
    [Fact]
    public async Task Returns_every_plan_the_store_holds_with_its_display_name_resolved()
    {
        await using var dbContext = CreateDbContext();
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5);
        dbContext.Plans.Add(plan);
        await dbContext.SaveChangesAsync();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(new ListPlansQuery(), CancellationToken.None);

        var dto = Assert.Single(result.Value.Items);
        Assert.Equal(plan.Id, dto.Id);
        Assert.Equal("PLANSTARTERNAME", dto.DisplayName);
        Assert.Equal(5_120, dto.DiskQuotaMb);
        Assert.Equal(5, dto.MaxSites);
        Assert.Equal(2, dto.MaxDatabases);
        Assert.Equal(3, dto.MaxSftpUsers);
    }

    /// <summary>With no sort named, plans come smallest disk quota first.</summary>
    [Fact]
    public async Task With_no_sort_named_plans_come_smallest_disk_quota_first()
    {
        await using var dbContext = CreateDbContext();
        var large = new Plan(Guid.NewGuid(), "large", 1_048_576, 500, 500, 100, 200, 100);
        var small = new Plan(Guid.NewGuid(), "small", 5_120, 5, 2, 3, 5, 5);
        var medium = new Plan(Guid.NewGuid(), "medium", 25_600, 25, 10, 10, 20, 20);
        // Inserted out of size order so the assertion cannot pass by insertion-order accident.
        dbContext.Plans.AddRange(large, small, medium);
        await dbContext.SaveChangesAsync();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(new ListPlansQuery(), CancellationToken.None);

        Assert.Equal([small.Id, medium.Id, large.Id], result.Value.Items.Select(dto =>
        {
            return dto.Id;
        }));
    }

    /// <summary>A plan an operator named is listed under its own name, not a resource lookup.</summary>
    [Fact]
    public async Task A_plan_an_operator_named_is_listed_under_its_own_name_not_a_resource_lookup()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Plans.Add(Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8));
        await dbContext.SaveChangesAsync();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(new ListPlansQuery(), CancellationToken.None);

        // The uppercasing double would have answered "AGENCY" had the handler gone to the resources.
        var dto = Assert.Single(result.Value.Items);
        Assert.Equal("Agency", dto.DisplayName);
        Assert.Equal(2, dto.MaxFtpUsers);
        Assert.Equal(3, dto.MaxCronEntries);
        Assert.Equal(8, dto.MaxPhpWorkersPerPool);
    }

    /// <summary>Each plan reports how many accounts are on it, and an unused plan reports zero.</summary>
    [Fact]
    public async Task Each_plan_reports_how_many_accounts_are_on_it_and_an_unused_plan_reports_zero()
    {
        await using var dbContext = CreateDbContext();
        var busy = Plan.Define(Guid.NewGuid(), "Busy", 5_120, 5, 2, 3, 0, 5, 5);
        var quiet = Plan.Define(Guid.NewGuid(), "Quiet", 10_240, 5, 2, 3, 0, 5, 5);
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        dbContext.Plans.AddRange(busy, quiet);
        dbContext.Accounts.AddRange(
            new Account(Guid.NewGuid(), "olive", "olive.example.com", busy.Id, created),
            new Account(Guid.NewGuid(), "pepper", "pepper.example.com", busy.Id, created));
        await dbContext.SaveChangesAsync();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(new ListPlansQuery(), CancellationToken.None);

        var busyDto = result.Value.Items.Single(dto =>
        {
            return dto.Id == busy.Id;
        });
        var quietDto = result.Value.Items.Single(dto =>
        {
            return dto.Id == quiet.Id;
        });
        Assert.Equal(2, busyDto.AccountCount);
        Assert.Equal(0, quietDto.AccountCount);
    }

    /// <summary>Two plans with the same disk are listed in a stable order.</summary>
    [Fact]
    public async Task Two_plans_with_the_same_disk_are_listed_in_a_stable_order()
    {
        await using var dbContext = CreateDbContext();
        var first = Plan.Define(Guid.Parse("00000000-0000-4000-8000-000000000001"), "B", 5_120, 5, 2, 3, 0, 5, 5);
        var second = Plan.Define(Guid.Parse("00000000-0000-4000-8000-000000000002"), "A", 5_120, 5, 2, 3, 0, 5, 5);
        dbContext.Plans.AddRange(second, first);
        await dbContext.SaveChangesAsync();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(new ListPlansQuery(), CancellationToken.None);

        Assert.Equal([first.Id, second.Id], result.Value.Items.Select(dto =>
        {
            return dto.Id;
        }));
    }

    /// <summary>
    /// A name sort orders by the name the caller reads, so a shipped plan sorts by its translation and
    /// not by its resource key.
    /// </summary>
    [Fact]
    public async Task A_name_sort_orders_by_the_name_the_caller_reads()
    {
        await using var dbContext = CreateDbContext();
        // Shipped keys upper-case to "ZKEY" and "AKEY" through the double; the operator's plan reads "Middle".
        var z = new Plan(Guid.NewGuid(), "zkey", 5_120, 5, 2, 3, 5, 5);
        var a = new Plan(Guid.NewGuid(), "akey", 25_600, 5, 2, 3, 5, 5);
        var middle = Plan.Define(Guid.NewGuid(), "Middle", 10_240, 5, 2, 3, 0, 5, 5);
        dbContext.Plans.AddRange(z, a, middle);
        await dbContext.SaveChangesAsync();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(
            new ListPlansQuery(Sort: SortRequest.Parse("name")), CancellationToken.None);

        Assert.Equal(["AKEY", "Middle", "ZKEY"], result.Value.Items.Select(dto =>
        {
            return dto.DisplayName;
        }));
    }

    /// <summary>An accounts sort, descending, puts the busiest plan first.</summary>
    [Fact]
    public async Task A_descending_accounts_sort_puts_the_busiest_plan_first()
    {
        await using var dbContext = CreateDbContext();
        var busy = Plan.Define(Guid.NewGuid(), "Busy", 50_000, 5, 2, 3, 0, 5, 5);
        var quiet = Plan.Define(Guid.NewGuid(), "Quiet", 5_120, 5, 2, 3, 0, 5, 5);
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        dbContext.Plans.AddRange(quiet, busy);
        dbContext.Accounts.Add(new Account(Guid.NewGuid(), "olive", "olive.example.com", busy.Id, created));
        await dbContext.SaveChangesAsync();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(
            new ListPlansQuery(Sort: SortRequest.Parse("-accounts")), CancellationToken.None);

        Assert.Equal([busy.Id, quiet.Id], result.Value.Items.Select(dto =>
        {
            return dto.Id;
        }));
    }

    /// <summary>
    /// A search matches the name the caller reads, ignoring case, and the total counts only the matches.
    /// </summary>
    [Fact]
    public async Task A_search_matches_the_name_the_caller_reads_and_the_total_counts_only_the_matches()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Plans.AddRange(
            new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5),
            Plan.Define(Guid.NewGuid(), "Agency", 10_240, 5, 2, 3, 0, 5, 5));
        await dbContext.SaveChangesAsync();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        // "starter" is found only in the resolved name "PLANSTARTERNAME", never in a column as typed.
        var result = await handler.HandleAsync(
            new ListPlansQuery(Search: SearchTerm.Of("starter")), CancellationToken.None);

        var dto = Assert.Single(result.Value.Items);
        Assert.Equal("PLANSTARTERNAME", dto.DisplayName);
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>A page holds only its slice, and carries the total across every page.</summary>
    [Fact]
    public async Task A_page_holds_only_its_slice_and_carries_the_total_across_every_page()
    {
        await using var dbContext = CreateDbContext();
        for (var index = 1; index <= 11; index++)
        {
            dbContext.Plans.Add(Plan.Define(Guid.NewGuid(), $"Tier{index:00}", index * 1_024, 5, 2, 3, 0, 5, 5));
        }

        await dbContext.SaveChangesAsync();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(new ListPlansQuery(Page: 2, PageSize: 10), CancellationToken.None);

        var dto = Assert.Single(result.Value.Items);
        Assert.Equal("Tier11", dto.DisplayName);
        Assert.Equal(11, result.Value.Total);
    }

    /// <summary>
    /// A deterministic <see cref="IStringLocalizer{T}"/> double that upper-cases the key it is
    /// given, so a test can assert the handler passed the plan's exact display-name key through
    /// without depending on the real resx text.
    /// </summary>
    private sealed class UppercasingDisplayNamesLocalizer : IStringLocalizer<DisplayNames>
    {
        /// <inheritdoc />
        public LocalizedString this[string name]
        {
            get
            {
                return new(name, name.ToUpperInvariant());
            }
        }

        /// <inheritdoc />
        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                return new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name.ToUpperInvariant(), arguments));
            }
        }

        /// <inheritdoc />
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return [];
        }
    }
}
