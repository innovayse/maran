using Maran.Sdk.Contracts;
using Maran.Sdk.Filters;
using Maran.Sdk.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Sdk.Tests.Filters;

/// <summary>
/// Behavioral contract of <see cref="RequiresLicensedModuleFilter"/>: a paid module's request runs only when
/// the licence window says the module is covered, and EVERY other answer, a missing window and a window that
/// throws included, refuses it with 403 and <c>ModuleLicenceRequired</c> before the action runs.
/// </summary>
public sealed class RequiresLicensedModuleFilterTests
{
    /// <summary>A covered module reaches the action and nothing is refused.</summary>
    [Fact]
    public async Task A_covered_module_reaches_the_action()
    {
        var run = await RunAsync(new FixedWindow(EntitlementVerdict.Covered));

        Assert.True(run.ActionRan);
        Assert.Null(run.Context.Result);
    }

    /// <summary>A module nothing covers, and one whose cover lapsed, are refused alike and the action never runs.</summary>
    [Theory]
    [InlineData(EntitlementVerdict.NotCovered)]
    [InlineData(EntitlementVerdict.Lapsed)]
    public async Task An_uncovered_or_lapsed_module_is_refused_and_the_action_never_runs(EntitlementVerdict verdict)
    {
        var run = await RunAsync(new FixedWindow(verdict));

        Assert.False(run.ActionRan);
        AssertRefused(run);
    }

    /// <summary>A host that composes no licence window refuses a paid module: absent means not covered.</summary>
    [Fact]
    public async Task A_missing_licence_window_refuses_the_paid_module()
    {
        var run = await RunAsync(window: null);

        Assert.False(run.ActionRan);
        AssertRefused(run);
    }

    /// <summary>A window that throws refuses the paid module and does not let the exception out of the filter.</summary>
    [Fact]
    public async Task A_throwing_licence_window_refuses_the_paid_module_without_throwing()
    {
        var run = await RunAsync(new ThrowingWindow());

        Assert.False(run.ActionRan);
        AssertRefused(run);
    }

    /// <summary>The filter asks the window about its own module id and nobody else's.</summary>
    [Fact]
    public async Task The_window_is_asked_about_the_filters_own_module()
    {
        var window = new FixedWindow(EntitlementVerdict.Covered);

        await RunAsync(window, moduleId: "dns");

        Assert.Equal(["dns"], window.Asked);
    }

    /// <summary>The attribute carries its module id to the filter as an argument.</summary>
    [Fact]
    public void The_attribute_carries_its_module_id_to_the_filter_as_an_argument()
    {
        var attribute = new RequiresLicensedModuleAttribute("mail");

        Assert.Equal("mail", attribute.ModuleId);
        Assert.Equal(typeof(RequiresLicensedModuleFilter), attribute.ImplementationType);
        Assert.Equal(new object[] { "mail" }, attribute.Arguments);
    }

    /// <summary>Asserts the run ended in the licence refusal.</summary>
    /// <param name="run">The run.</param>
    private static void AssertRefused(FilterRun run)
    {
        var refusal = Assert.IsType<ObjectResult>(run.Context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(refusal.Value);
        Assert.Equal("ModuleLicenceRequired", problem.Extensions["code"]);
    }

    /// <summary>Runs the filter against a window (or none).</summary>
    /// <param name="window">The licence window to register, or null for none.</param>
    /// <param name="moduleId">The module the filter guards.</param>
    /// <returns>What happened.</returns>
    private static async Task<FilterRun> RunAsync(ILicenceEntitlements? window, string moduleId = "mail")
    {
        var services = new ServiceCollection();
        if (window is not null)
        {
            services.AddSingleton(window);
        }

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(
            actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var ran = false;

        await new RequiresLicensedModuleFilter(moduleId).OnActionExecutionAsync(executing, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });

        return new FilterRun(executing, ran);
    }

    /// <summary>What one filter run produced.</summary>
    /// <param name="Context">The context, whose result the filter may have set.</param>
    /// <param name="ActionRan">Whether the action delegate was invoked.</param>
    private sealed record FilterRun(ActionExecutingContext Context, bool ActionRan);

    /// <summary>A window answering one verdict for every module, remembering what it was asked.</summary>
    private sealed class FixedWindow : ILicenceEntitlements
    {
        /// <summary>What every question answers.</summary>
        private readonly EntitlementVerdict _verdict;

        /// <summary>Creates the window.</summary>
        /// <param name="verdict">What every question answers.</param>
        public FixedWindow(EntitlementVerdict verdict)
        {
            _verdict = verdict;
        }

        /// <summary>The module ids asked about, in order.</summary>
        public List<string> Asked { get; } = [];

        /// <inheritdoc />
        public Task<ModuleEntitlement> GetAsync(string moduleId, CancellationToken cancellationToken)
        {
            Asked.Add(moduleId);

            return Task.FromResult(new ModuleEntitlement(moduleId, _verdict, null));
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ModuleEntitlement>> ListAsync(
            IReadOnlyCollection<string> moduleIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<ModuleEntitlement> all = moduleIds
                .Select(moduleId => { return new ModuleEntitlement(moduleId, _verdict, null); })
                .ToList();

            return Task.FromResult(all);
        }
    }

    /// <summary>A window that breaks its own contract by throwing.</summary>
    private sealed class ThrowingWindow : ILicenceEntitlements
    {
        /// <inheritdoc />
        public Task<ModuleEntitlement> GetAsync(string moduleId, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("planted window failure");
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ModuleEntitlement>> ListAsync(
            IReadOnlyCollection<string> moduleIds,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("planted window failure");
        }
    }
}
