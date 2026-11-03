using Maran.Modules.Accounts.Commands.UpdatePlan;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.UpdatePlan;

/// <summary>
/// How a plan edit treats the feature set (issue #316): a stated list replaces it, a missing list
/// is refused, and a change is journalled so an operator can see who took a feature away.
/// </summary>
public sealed class UpdatePlanFeaturesTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The agent double; no quota or ceiling here ever changes, so it is never called.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>The display names of the plans the panel ships.</summary>
    private readonly StubDisplayNames _displayNames = new();

    /// <summary>The plan every test edits: it sells every feature.</summary>
    private readonly Plan _plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>Stores the plan the tests edit.</summary>
    public UpdatePlanFeaturesTests()
    {
        _dbContext.Plans.Add(_plan);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>A stated list replaces the set and the answer reports the new list.</summary>
    [Fact]
    public async Task A_stated_list_replaces_the_set_and_the_answer_reports_it()
    {
        var result = await NewHandler().HandleAsync(
            Edit([AccountFeature.Databases, AccountFeature.Backups]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([AccountFeature.Databases, AccountFeature.Backups], result.Value!.Features);
        var stored = await _dbContext.Plans.AsNoTracking().SingleAsync();
        Assert.Equal(AccountFeature.Databases | AccountFeature.Backups, stored.Features);
    }

    /// <summary>An empty list sells no feature at all.</summary>
    [Fact]
    public async Task An_empty_list_sells_no_feature_at_all()
    {
        var result = await NewHandler().HandleAsync(Edit([]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Features);
        Assert.Equal(AccountFeature.None, (await _dbContext.Plans.AsNoTracking().SingleAsync()).Features);
    }

    /// <summary>A missing list is refused by the validator: there is no reading of "not stated".</summary>
    [Fact]
    public async Task A_missing_list_is_refused_by_the_validator()
    {
        var verdict = await new UpdatePlanCommandValidator().ValidateAsync(Edit(null!));

        Assert.False(verdict.IsValid);
        Assert.Contains(verdict.Errors, error =>
        {
            return error.PropertyName == nameof(UpdatePlanCommand.Features);
        });
    }

    /// <summary>A change of features is journalled with the set before and after.</summary>
    [Fact]
    public async Task A_change_of_features_is_journalled_with_the_set_before_and_after()
    {
        _plan.ChangeFeatures(AccountFeature.Mail | AccountFeature.Dns);
        await _dbContext.SaveChangesAsync();

        await NewHandler().HandleAsync(Edit([AccountFeature.Mail]), CancellationToken.None);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AccountAuditActions.PlanUpdated, entry.Action);
        Assert.Equal("Agency [features: Mail, Dns -> Mail]", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Stating the set the plan already has journals no feature change.</summary>
    [Fact]
    public async Task Stating_the_set_the_plan_already_has_journals_no_feature_change()
    {
        await NewHandler().HandleAsync(Edit(AccountFeature.All.Members()), CancellationToken.None);

        Assert.Equal("Agency", Assert.Single(_audit.Entries).Subject);
    }

    /// <summary>Builds the handler over this test's context, agent and journal.</summary>
    /// <returns>The handler.</returns>
    private UpdatePlanCommandHandler NewHandler()
    {
        return new UpdatePlanCommandHandler(
            _dbContext,
            new AccountDiskQuotaEnforcer(_agent),
            new AccountRuntimeLimitsEnforcer(_agent),
            new PlanNameDirectory(_dbContext, _displayNames),
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            _displayNames);
    }

    /// <summary>Builds an edit of the plan that changes nothing but the features it states.</summary>
    /// <param name="features">The features to state; a test passes <c>null!</c> to stand for a request that left them out.</param>
    /// <returns>The command.</returns>
    private UpdatePlanCommand Edit(IReadOnlyList<AccountFeature> features)
    {
        return new UpdatePlanCommand(
            "Agency", 10_240, 10, 5, 4, 2, 3, 8,
            ResourceCeilingTestValues.Unlimited, ResourceCeilingTestValues.Unlimited, ResourceCeilingTestValues.Unlimited,
            features, _plan.Id, "203.0.113.7", "unit-tests");
    }
}
