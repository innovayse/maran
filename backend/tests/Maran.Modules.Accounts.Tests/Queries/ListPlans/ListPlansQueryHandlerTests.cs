using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.ListPlans;
using Maran.Modules.Accounts.Resources;
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

    /// <summary>Empty store returns an empty list.</summary>
    [Fact]
    public async Task Empty_store_returns_an_empty_list()
    {
        await using var dbContext = CreateDbContext();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(new ListPlansQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    /// <summary>Returns every plan the store holds with its display name resolved.</summary>
    [Fact]
    public async Task Returns_every_plan_the_store_holds_with_its_display_name_resolved()
    {
        await using var dbContext = CreateDbContext();
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3);
        dbContext.Plans.Add(plan);
        await dbContext.SaveChangesAsync();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(new ListPlansQuery(), CancellationToken.None);

        var dto = Assert.Single(result.Value);
        Assert.Equal(plan.Id, dto.Id);
        Assert.Equal("PLANSTARTERNAME", dto.DisplayName);
        Assert.Equal(5_120, dto.DiskQuotaMb);
        Assert.Equal(5, dto.MaxSites);
        Assert.Equal(2, dto.MaxDatabases);
        Assert.Equal(3, dto.MaxFtpUsers);
    }

    /// <summary>Returns plans ordered by disk quota ascending.</summary>
    [Fact]
    public async Task Returns_plans_ordered_by_disk_quota_ascending()
    {
        await using var dbContext = CreateDbContext();
        var large = new Plan(Guid.NewGuid(), "large", 1_048_576, 500, 500, 100);
        var small = new Plan(Guid.NewGuid(), "small", 5_120, 5, 2, 3);
        var medium = new Plan(Guid.NewGuid(), "medium", 25_600, 25, 10, 10);
        // Inserted out of size order so the assertion cannot pass by insertion-order accident.
        dbContext.Plans.AddRange(large, small, medium);
        await dbContext.SaveChangesAsync();
        var handler = new ListPlansQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer());

        var result = await handler.HandleAsync(new ListPlansQuery(), CancellationToken.None);

        Assert.Equal([small.Id, medium.Id, large.Id], result.Value.Select(dto =>
        {
            return dto.Id;
        }));
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
