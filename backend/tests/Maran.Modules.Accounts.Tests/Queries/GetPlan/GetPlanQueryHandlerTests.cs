using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.GetPlan;
using Maran.Modules.Accounts.Resources;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Tests.Queries.GetPlan;

/// <summary>Behavioral contract of <see cref="GetPlanQueryHandler"/>, against the EF Core InMemory provider.</summary>
public sealed class GetPlanQueryHandlerTests
{
    /// <summary>A plan is read with its resolved name and how many accounts are on it.</summary>
    [Fact]
    public async Task A_plan_is_read_with_its_resolved_name_and_how_many_accounts_are_on_it()
    {
        await using var dbContext = CreateDbContext();
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5);
        var other = Plan.Define(Guid.NewGuid(), "Other", 10_240, 5, 2, 3, 0, 5, 5);
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        dbContext.Plans.AddRange(plan, other);
        dbContext.Accounts.AddRange(
            new Account(Guid.NewGuid(), "olive", "olive.example.com", plan.Id, created),
            new Account(Guid.NewGuid(), "pepper", "pepper.example.com", other.Id, created));
        await dbContext.SaveChangesAsync();

        var result = await new GetPlanQueryHandler(dbContext, new EchoingDisplayNamesLocalizer())
            .HandleAsync(new GetPlanQuery(plan.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(plan.Id, result.Value.Id);
        Assert.Equal("resolved:PlanStarterName", result.Value.DisplayName);
        Assert.Equal(1, result.Value.AccountCount);
    }

    /// <summary>A plan that does not exist answers not found.</summary>
    [Fact]
    public async Task A_plan_that_does_not_exist_answers_not_found()
    {
        await using var dbContext = CreateDbContext();

        var result = await new GetPlanQueryHandler(dbContext, new EchoingDisplayNamesLocalizer())
            .HandleAsync(new GetPlanQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
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

    /// <summary>A localizer double prefixing every key, so a resolved name is told from a stored one.</summary>
    private sealed class EchoingDisplayNamesLocalizer : IStringLocalizer<DisplayNames>
    {
        /// <inheritdoc />
        public LocalizedString this[string name]
        {
            get
            {
                return new(name, "resolved:" + name);
            }
        }

        /// <inheritdoc />
        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                return new(name, "resolved:" + name);
            }
        }

        /// <inheritdoc />
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return [];
        }
    }
}
