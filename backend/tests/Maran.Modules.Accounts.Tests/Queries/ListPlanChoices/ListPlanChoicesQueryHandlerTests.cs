using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.ListPlanChoices;
using Maran.Modules.Accounts.Resources;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Tests.Queries.ListPlanChoices;

/// <summary>
/// Behavioral contract of <see cref="ListPlanChoicesQueryHandler"/>, against the EF Core InMemory
/// provider with a localizer double that upper-cases a shipped plan's key, so a test can tell the
/// resolved name from the stored one.
/// </summary>
public sealed class ListPlanChoicesQueryHandlerTests
{
    /// <summary>A choice carries the plan's identity, its resolved name and the two limits a picker prints.</summary>
    [Fact]
    public async Task A_choice_carries_the_identity_the_resolved_name_and_the_two_limits_a_picker_prints()
    {
        await using var dbContext = CreateDbContext();
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5);
        dbContext.Plans.Add(plan);
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListPlanChoicesQuery());

        var choice = Assert.Single(result.Value.Items);
        Assert.Equal(plan.Id, choice.Id);
        Assert.Equal("PLANSTARTERNAME", choice.DisplayName);
        Assert.Equal(5_120, choice.DiskQuotaMb);
        Assert.Equal(5, choice.MaxSites);
    }

    /// <summary>With no sort named, choices come alphabetically by the name the caller reads.</summary>
    [Fact]
    public async Task With_no_sort_named_choices_come_alphabetically_by_the_name_the_caller_reads()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Plans.AddRange(
            Plan.Define(Guid.NewGuid(), "Zinc", 1_024, 5, 2, 3, 0, 5, 5),
            new Plan(Guid.NewGuid(), "bkey", 2_048, 5, 2, 3, 5, 5),
            Plan.Define(Guid.NewGuid(), "agency", 4_096, 5, 2, 3, 0, 5, 5));
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListPlanChoicesQuery());

        Assert.Equal(["agency", "BKEY", "Zinc"], result.Value.Items.Select(choice =>
        {
            return choice.DisplayName;
        }));
    }

    /// <summary>A search narrows the choices and the total counts only the matches.</summary>
    [Fact]
    public async Task A_search_narrows_the_choices_and_the_total_counts_only_the_matches()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Plans.AddRange(
            Plan.Define(Guid.NewGuid(), "Agency", 1_024, 5, 2, 3, 0, 5, 5),
            Plan.Define(Guid.NewGuid(), "Studio", 2_048, 5, 2, 3, 0, 5, 5));
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListPlanChoicesQuery(Search: SearchTerm.Of("GEN")));

        Assert.Equal("Agency", Assert.Single(result.Value.Items).DisplayName);
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>Builds a fresh, isolated in-memory <see cref="AccountsDbContext"/>.</summary>
    /// <returns>The context.</returns>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Runs the handler with the upper-casing localizer.</summary>
    /// <param name="dbContext">The context to read.</param>
    /// <param name="query">The picker request.</param>
    /// <returns>The handler's answer.</returns>
    private static Task<Result<PagedResult<PlanChoiceDto>>> HandleAsync(
        AccountsDbContext dbContext,
        ListPlanChoicesQuery query)
    {
        return new ListPlanChoicesQueryHandler(dbContext, new UppercasingDisplayNamesLocalizer())
            .HandleAsync(query, CancellationToken.None);
    }

    /// <summary>A localizer double answering every key upper-cased.</summary>
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
                return new(name, name.ToUpperInvariant());
            }
        }

        /// <inheritdoc />
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return [];
        }
    }
}
