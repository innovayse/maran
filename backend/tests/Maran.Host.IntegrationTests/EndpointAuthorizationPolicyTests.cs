using System.Reflection;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Host.Modules;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The route table the composed host actually serves, read endpoint by endpoint: every one either
/// names one of the panel's two policies or is marked anonymous and listed, with a reason, in
/// <see cref="AnonymousEndpointPin"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes the administrator-only fallback a safety net rather than something the
/// panel's own endpoints lean on. The fallback refuses a customer when an endpoint says nothing;
/// this class fails the build when an endpoint says nothing, or says it in a way that reads as a
/// policy and is not (a bare <c>[Authorize]</c>, a role list, a misspelt policy name).
/// </para>
/// <para>
/// <b>The walk is held against going blind.</b> A census over an empty or partial route table
/// passes. The completeness test therefore compares the walk against the controller actions the
/// registered modules declare, and against the host's own minimal endpoints, which no controller
/// reflection can see. The rule itself is proven on hand-built endpoints that break it in each
/// way, and on ones that obey it.
/// </para>
/// <para>
/// UNOBSERVED HERE: this reads metadata, not behaviour. A wrong-but-named policy (an administrator
/// policy where the customer one was meant) passes every census test; the per-endpoint HTTP tests
/// are what see that. Controller declarations are also read by reflection in the architecture
/// tests, which cannot see the minimal endpoints.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class EndpointAuthorizationPolicyTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public EndpointAuthorizationPolicyTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Every served endpoint names its policy or is a pinned anonymous one.</summary>
    [Fact]
    public async Task Every_endpoint_names_its_policy_or_is_marked_anonymous()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);

        var violations = EndpointAuthorizationCensus.FindViolations(RealEndpoints(factory), AnonymousEndpointPin.All);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>The anonymous endpoints are exactly the pinned set, in both directions.</summary>
    [Fact]
    public async Task The_anonymous_endpoints_are_exactly_the_pinned_set()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var endpoints = RealEndpoints(factory);

        var stale = EndpointAuthorizationCensus.FindStalePins(endpoints, AnonymousEndpointPin.All);
        var anonymous = endpoints
            .Where(endpoint =>
            {
                return endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            })
            .Select(EndpointAuthorizationCensus.RouteKey)
            .Order(StringComparer.Ordinal)
            .ToList();
        var pinned = AnonymousEndpointPin.All.Select(entry =>
        {
            return entry.Route;
        }).Order(StringComparer.Ordinal).ToList();

        Assert.True(stale.Count == 0, string.Join(Environment.NewLine, stale));
        Assert.Equal(pinned, anonymous);
    }

    /// <summary>
    /// The walk saw every action the registered modules declare and every endpoint the host maps
    /// itself.
    /// </summary>
    /// <remarks>
    /// The vacuity guard. A walk that lost the controller endpoints but kept the minimal ones would
    /// satisfy any count floor and judge nothing, so completeness is asked per action and per host
    /// route.
    /// </remarks>
    [Fact]
    public async Task The_walk_saw_every_controller_action_and_every_host_endpoint()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var endpoints = RealEndpoints(factory);

        var walkedActions = endpoints
            .Select(endpoint =>
            {
                return endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.MethodInfo;
            })
            .OfType<MethodInfo>()
            .ToHashSet();
        var declaredActions = ModuleRegistry.All
            .Select(module =>
            {
                return module.GetType().Assembly;
            })
            .Distinct()
            .SelectMany(assembly =>
            {
                return assembly.GetTypes();
            })
            .Where(type =>
            {
                return typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract;
            })
            .SelectMany(type =>
            {
                return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            })
            .Where(method =>
            {
                return method.GetCustomAttributes(inherit: true).OfType<IActionHttpMethodProvider>().Any();
            })
            .ToList();
        var missing = declaredActions
            .Where(method =>
            {
                return !walkedActions.Contains(method);
            })
            .Select(method =>
            {
                return $"{method.DeclaringType!.Name}.{method.Name}";
            })
            .ToList();

        Assert.NotEmpty(declaredActions);
        Assert.True(missing.Count == 0, "Actions with no endpoint in the route table: " + string.Join(", ", missing));

        var keys = endpoints.Select(EndpointAuthorizationCensus.RouteKey).ToHashSet(StringComparer.Ordinal);
        foreach (var hostRoute in new[]
        {
            "GET /health",
            "GET /health/live",
            "GET /health/ready",
            "GET /api/v1/modules",
            "GET /api/v1/dashboard",
        })
        {
            Assert.Contains(hostRoute, keys);
        }
    }

    /// <summary>An endpoint with no authorization metadata at all is reported.</summary>
    [Fact]
    public void The_census_reports_an_endpoint_with_no_authorization_metadata()
    {
        var violations = EndpointAuthorizationCensus.FindViolations([Planted()], []);

        var violation = Assert.Single(violations);
        Assert.StartsWith("GET /planted/x:", violation, StringComparison.Ordinal);
        Assert.Contains("declares no authorization", violation, StringComparison.Ordinal);
    }

    /// <summary>A bare authorize declaration, which names no policy, is reported.</summary>
    [Fact]
    public void The_census_reports_a_bare_authorize()
    {
        var violations = EndpointAuthorizationCensus.FindViolations([Planted(new AuthorizeAttribute())], []);

        var violation = Assert.Single(violations);
        Assert.Contains("names no policy", violation, StringComparison.Ordinal);
    }

    /// <summary>A roles-only authorize declaration is reported as restricting by roles.</summary>
    [Fact]
    public void The_census_reports_a_role_only_authorize()
    {
        var violations = EndpointAuthorizationCensus.FindViolations(
            [Planted(new AuthorizeAttribute { Roles = "Admin" })],
            []);

        Assert.Contains(violations, violation =>
        {
            return violation.Contains("restricts by roles", StringComparison.Ordinal);
        });
    }

    /// <summary>A misspelt policy name is reported with the name it carried.</summary>
    [Fact]
    public void The_census_reports_an_unknown_policy_name()
    {
        var violations = EndpointAuthorizationCensus.FindViolations(
            [Planted(new AuthorizeAttribute { Policy = "AdminOnIy" })],
            []);

        var violation = Assert.Single(violations);
        Assert.Contains("AdminOnIy", violation, StringComparison.Ordinal);
    }

    /// <summary>A policy object, which carries no name to review, is reported.</summary>
    [Fact]
    public void The_census_reports_a_policy_object_without_a_name()
    {
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        var violations = EndpointAuthorizationCensus.FindViolations([Planted(policy)], []);

        Assert.Contains(violations, violation =>
        {
            return violation.Contains("policy object", StringComparison.Ordinal);
        });
    }

    /// <summary>An anonymous endpoint missing from the pin is reported.</summary>
    [Fact]
    public void The_census_reports_an_anonymous_endpoint_missing_from_the_pin()
    {
        var violations = EndpointAuthorizationCensus.FindViolations([Planted(new AllowAnonymousAttribute())], []);

        var violation = Assert.Single(violations);
        Assert.Contains("not in the anonymous pin", violation, StringComparison.Ordinal);
    }

    /// <summary>A pinned route that is no longer anonymous, or that has no reason, is reported.</summary>
    [Fact]
    public void The_census_reports_a_pinned_endpoint_that_is_no_longer_anonymous()
    {
        var endpoints = new[] { Planted(new AuthorizeAttribute { Policy = AuthorizationPolicies.AdminOnly }) };

        var notAnonymous = EndpointAuthorizationCensus.FindStalePins(
            endpoints,
            [new PinnedAnonymousEndpoint("GET /planted/x", "was open once")]);
        var noReason = EndpointAuthorizationCensus.FindStalePins(
            [Planted(new AllowAnonymousAttribute())],
            [new PinnedAnonymousEndpoint("GET /planted/x", " ")]);
        var nowhere = EndpointAuthorizationCensus.FindStalePins(
            endpoints,
            [new PinnedAnonymousEndpoint("GET /planted/gone", "no such route")]);

        Assert.Contains(notAnonymous, entry =>
        {
            return entry.Contains("not an anonymous endpoint", StringComparison.Ordinal);
        });
        Assert.Contains(noReason, entry =>
        {
            return entry.Contains("without a reason", StringComparison.Ordinal);
        });
        Assert.Contains(nowhere, entry =>
        {
            return entry.Contains("GET /planted/gone", StringComparison.Ordinal);
        });
    }

    /// <summary>An endpoint naming either known policy, alone or doubled on class and action, is accepted.</summary>
    [Fact]
    public void The_census_accepts_an_endpoint_naming_a_known_policy()
    {
        var admin = Planted(new AuthorizeAttribute { Policy = AuthorizationPolicies.AdminOnly });
        var customer = Planted(new AuthorizeAttribute { Policy = AuthorizationPolicies.AnyAuthenticated });
        var doubled = Planted(
            new AuthorizeAttribute { Policy = AuthorizationPolicies.AnyAuthenticated },
            new AuthorizeAttribute { Policy = AuthorizationPolicies.AnyAuthenticated });

        var violations = EndpointAuthorizationCensus.FindViolations([admin, customer, doubled], []);

        Assert.Empty(violations);
    }

    /// <summary>A pinned anonymous endpoint is accepted, and its pin is not stale.</summary>
    [Fact]
    public void The_census_accepts_a_pinned_anonymous_endpoint()
    {
        var endpoints = new[] { Planted(new AllowAnonymousAttribute()) };
        var pin = new[] { new PinnedAnonymousEndpoint("GET /planted/x", "a planted reason") };

        Assert.Empty(EndpointAuthorizationCensus.FindViolations(endpoints, pin));
        Assert.Empty(EndpointAuthorizationCensus.FindStalePins(endpoints, pin));
    }

    /// <summary>Every route endpoint the booted host serves.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The route endpoints, in the order the table holds them.</returns>
    private static List<RouteEndpoint> RealEndpoints(WebApplicationFactory<Program> factory)
    {
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
    }

    /// <summary>Builds a <c>GET /planted/x</c> endpoint carrying exactly the given metadata.</summary>
    /// <param name="metadata">The metadata objects the endpoint carries besides its verb.</param>
    /// <returns>The planted endpoint.</returns>
    private static RouteEndpoint Planted(params object[] metadata)
    {
        var all = new List<object> { new HttpMethodMetadata(["GET"]) };
        all.AddRange(metadata);

        return new RouteEndpoint(
            _ =>
            {
                return Task.CompletedTask;
            },
            RoutePatternFactory.Parse("/planted/x"),
            order: 0,
            new EndpointMetadataCollection(all),
            displayName: "planted");
    }
}
