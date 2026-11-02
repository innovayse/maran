using Maran.Sdk.Contracts;
using Maran.Sdk.Filters;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Sdk.Tests.Filters;

/// <summary>
/// Behavioral contract of <see cref="RequiresAccountFeatureFilter"/>: a customer whose plan lacks
/// the feature is refused before the action runs, whatever the menu shows, and an administrator is
/// never affected.
/// </summary>
public sealed class RequiresAccountFeatureFilterTests
{
    /// <summary>The account the customer acts for.</summary>
    private static readonly Guid AccountId = Guid.Parse("33333333-0000-4000-8000-000000000001");

    /// <summary>A customer whose plan includes the feature reaches the action.</summary>
    [Fact]
    public async Task A_customer_whose_plan_includes_the_feature_reaches_the_action()
    {
        var run = await RunAsync(new Caller(AccountId, false), included: true);

        Assert.True(run.ActionRan);
        Assert.Null(run.Context.Result);
    }

    /// <summary>A member the plan covers but the owner did not give the feature to is refused with its own code, and the action never runs.</summary>
    [Fact]
    public async Task A_person_who_was_not_given_the_feature_is_refused_whatever_the_plan_includes()
    {
        var run = await RunAsync(new Caller(AccountId, false), included: true, permitted: false);

        Assert.False(run.ActionRan);
        var refusal = Assert.IsType<ObjectResult>(run.Context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(refusal.Value);
        Assert.Equal("AccountFeatureNotGranted", problem.Extensions["code"]);
    }

    /// <summary>The plan is asked first: a plan that lacks the feature is reported as the plan's, not the person's.</summary>
    [Fact]
    public async Task The_plan_refusal_comes_before_the_person_is_asked()
    {
        var run = await RunAsync(new Caller(AccountId, false), included: false, permitted: false);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(run.Context.Result).Value);
        Assert.Equal("PlanFeatureNotIncluded", problem.Extensions["code"]);
        Assert.Empty(run.Grants.Asked);
    }

    /// <summary>The person is asked about their own identity, the account their token names and the filter's feature.</summary>
    [Fact]
    public async Task The_person_is_asked_about_their_own_identity_account_and_feature()
    {
        var caller = new Caller(AccountId, false);

        var run = await RunAsync(caller, included: true, feature: AccountFeature.Backups);

        Assert.Equal((caller.UserId, AccountId, AccountFeature.Backups), Assert.Single(run.Grants.Asked));
    }

    /// <summary>A customer whose plan lacks the feature is refused with 403 and a coded problem, and the action never runs.</summary>
    [Fact]
    public async Task A_customer_whose_plan_lacks_the_feature_is_refused_and_the_action_never_runs()
    {
        var run = await RunAsync(new Caller(AccountId, false), included: false);

        Assert.False(run.ActionRan);
        var refusal = Assert.IsType<ObjectResult>(run.Context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(refusal.Value);
        Assert.Equal("PlanFeatureNotIncluded", problem.Extensions["code"]);
    }

    /// <summary>The filter asks about the caller's own account and the feature it was built for.</summary>
    [Fact]
    public async Task The_filter_asks_about_the_callers_own_account_and_its_own_feature()
    {
        var run = await RunAsync(new Caller(AccountId, false), included: true, AccountFeature.Backups);

        Assert.Equal((AccountId, AccountFeature.Backups), Assert.Single(run.Directory.Asked));
    }

    /// <summary>An administrator is never refused and the directory is not even asked.</summary>
    [Fact]
    public async Task An_administrator_is_never_refused_and_the_directory_is_not_asked()
    {
        var run = await RunAsync(new Caller(null, true), included: false);

        Assert.True(run.ActionRan);
        Assert.Empty(run.Directory.Asked);
        Assert.Empty(run.Grants.Asked);
    }

    /// <summary>A caller with no account is passed on to the handler, which answers in its own words.</summary>
    [Fact]
    public async Task A_caller_with_no_account_is_passed_on_to_the_handler()
    {
        var run = await RunAsync(new Caller(null, false), included: false);

        Assert.True(run.ActionRan);
        Assert.Empty(run.Directory.Asked);
    }

    /// <summary>The attribute carries its feature to the filter as an argument.</summary>
    [Fact]
    public void The_attribute_carries_its_feature_to_the_filter_as_an_argument()
    {
        var attribute = new RequiresAccountFeatureAttribute(AccountFeature.Cron);

        Assert.Equal(AccountFeature.Cron, attribute.Feature);
        Assert.Equal(typeof(RequiresAccountFeatureFilter), attribute.ImplementationType);
        Assert.Equal(new object[] { AccountFeature.Cron }, attribute.Arguments);
    }

    /// <summary>Runs the filter for a caller against a directory that answers <paramref name="included"/>.</summary>
    /// <param name="caller">The caller.</param>
    /// <param name="included">What the directory answers.</param>
    /// <param name="feature">The feature the filter guards.</param>
    /// <returns>What happened.</returns>
    private static async Task<FilterRun> RunAsync(
        Caller caller,
        bool included,
        AccountFeature feature = AccountFeature.Databases,
        bool permitted = true)
    {
        var directory = new RecordingDirectory(included);
        var grants = new RecordingGrants(permitted);
        var filter = new RequiresAccountFeatureFilter(feature, caller, directory, grants);
        var httpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(
            actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var ran = false;

        await filter.OnActionExecutionAsync(executing, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });

        return new FilterRun(executing, directory, grants, ran);
    }

    /// <summary>What one filter run produced.</summary>
    /// <param name="Context">The context, whose result the filter may have set.</param>
    /// <param name="Directory">The directory the filter asked.</param>
    /// <param name="Grants">The per-person window the filter asked.</param>
    /// <param name="ActionRan">Whether the action delegate was invoked.</param>
    private sealed record FilterRun(
        ActionExecutingContext Context,
        RecordingDirectory Directory,
        RecordingGrants Grants,
        bool ActionRan);

    /// <summary>A caller with a fixed identity.</summary>
    private sealed class Caller : ICurrentUser
    {
        /// <inheritdoc />
        public Guid UserId { get; } = Guid.NewGuid();

        /// <inheritdoc />
        public string Username { get; } = "caller";

        /// <inheritdoc />
        public Guid? AccountId { get; }

        /// <inheritdoc />
        public bool IsAdmin { get; }

        /// <summary>Creates the caller.</summary>
        /// <param name="accountId">The account the token names, or <c>null</c>.</param>
        /// <param name="isAdmin">Whether the caller is an administrator.</param>
        public Caller(Guid? accountId, bool isAdmin)
        {
            AccountId = accountId;
            IsAdmin = isAdmin;
        }
    }

    /// <summary>A directory that answers one fixed value and records what it was asked.</summary>
    private sealed class RecordingDirectory : IAccountFeatureDirectory
    {
        /// <summary>The fixed answer.</summary>
        private readonly bool _included;

        /// <summary>Every question asked, in order.</summary>
        public List<(Guid AccountId, AccountFeature Feature)> Asked { get; } = [];

        /// <summary>Creates the directory.</summary>
        /// <param name="included">The answer to every question.</param>
        public RecordingDirectory(bool included)
        {
            _included = included;
        }

        /// <inheritdoc />
        public Task<bool> IncludesAsync(Guid accountId, AccountFeature feature, CancellationToken cancellationToken)
        {
            Asked.Add((accountId, feature));
            return Task.FromResult(_included);
        }
    }

    /// <summary>A per-person window that answers one fixed value and records what it was asked.</summary>
    private sealed class RecordingGrants : IAccountMemberGrants
    {
        /// <summary>The fixed answer.</summary>
        private readonly bool _permitted;

        /// <summary>Every question asked, in order.</summary>
        public List<(Guid UserId, Guid AccountId, AccountFeature Feature)> Asked { get; } = [];

        /// <summary>Creates the window.</summary>
        /// <param name="permitted">The answer to every question.</param>
        public RecordingGrants(bool permitted)
        {
            _permitted = permitted;
        }

        /// <inheritdoc />
        public Task<bool> PermitsAsync(
            Guid userId,
            Guid accountId,
            AccountFeature feature,
            CancellationToken cancellationToken)
        {
            Asked.Add((userId, accountId, feature));
            return Task.FromResult(_permitted);
        }
    }
}
