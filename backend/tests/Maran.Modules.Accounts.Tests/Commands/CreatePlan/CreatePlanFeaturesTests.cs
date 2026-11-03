using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreatePlan;

/// <summary>How a plan creation treats the feature set (issue #316).</summary>
public sealed class CreatePlanFeaturesTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>The display names of the plans the panel ships.</summary>
    private readonly StubDisplayNames _displayNames = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>A plan created with a list sells exactly those features.</summary>
    [Fact]
    public async Task A_plan_created_with_a_list_sells_exactly_those_features()
    {
        var result = await NewHandler().HandleAsync(Command([AccountFeature.Dns, AccountFeature.Cron]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([AccountFeature.Dns, AccountFeature.Cron], result.Value!.Features);
        Assert.Equal(AccountFeature.Dns | AccountFeature.Cron, (await _dbContext.Plans.SingleAsync()).Features);
    }

    /// <summary>A plan created with every feature stated sells the full set.</summary>
    [Fact]
    public async Task A_plan_created_with_every_feature_stated_sells_the_full_set()
    {
        var result = await NewHandler().HandleAsync(Command(AccountFeature.All.Members()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountFeature.All, (await _dbContext.Plans.SingleAsync()).Features);
    }

    /// <summary>The validator refuses a value that is not exactly one feature and refuses a missing list.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(3L)]
    [InlineData(63L)]
    [InlineData(1L << 40)]
    public async Task The_validator_refuses_a_value_that_is_not_exactly_one_feature(long raw)
    {
        var validator = new CreatePlanCommandValidator();

        var refused = await validator.ValidateAsync(Command([(AccountFeature)raw]));
        var accepted = await validator.ValidateAsync(Command([AccountFeature.Mail]));
        var missing = await validator.ValidateAsync(Command(null!));

        Assert.False(refused.IsValid);
        Assert.Contains(refused.Errors, error =>
        {
            return error.ErrorMessage == "PlanFeatureUnknown";
        });
        Assert.True(accepted.IsValid);
        Assert.False(missing.IsValid);
    }

    /// <summary>Builds the handler over this test's context.</summary>
    /// <returns>The handler.</returns>
    private CreatePlanCommandHandler NewHandler()
    {
        return PlanHandlerFactory.Create(_dbContext, FakeCurrentUser.Admin(), new RecordingAuditWriter(), _displayNames);
    }

    /// <summary>Builds a creation request stating these features.</summary>
    /// <param name="features">The features to state; a test passes <c>null!</c> to stand for a request that left them out.</param>
    /// <returns>The command.</returns>
    private static CreatePlanCommand Command(IReadOnlyList<AccountFeature> features)
    {
        return new(
            "Agency", 10_240, 10, 5, 4, 2, 3, 8,
            ResourceCeilingTestValues.Unlimited, ResourceCeilingTestValues.Unlimited, ResourceCeilingTestValues.Unlimited,
            features, "203.0.113.7", "unit-tests");
    }
}
