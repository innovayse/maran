using System.Net;
using Maran.Host.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What the running pipeline does with a request whose endpoint carries no authorization metadata,
/// and with one that matches no endpoint or the wrong verb: the host's fallback policy, observed over
/// real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// The fallback is administrator-only. These tests show it where it matters: an anonymous caller is
/// 401, a signed-in customer is 403 with an EMPTY body (the authorization layer refused, no handler
/// ran), and an administrator is admitted. The administrator case is the inverse control: a fallback
/// that refused everyone would pass the other two.
/// </para>
/// <para>
/// The unmarked endpoint is <see cref="UnmarkedProbeController"/>, added to this class's hosts only.
/// <see cref="The_probe_endpoint_carries_no_authorization_metadata"/> reads its metadata first, so a
/// future attribute on the probe or its base cannot silently turn these into tests of that attribute.
/// </para>
/// <para>
/// UNOBSERVED HERE: the policy objects themselves are held by the unit tests of the host's
/// authorization setup, and which panel endpoints are unmarked (none) by
/// <see cref="EndpointAuthorizationPolicyTests"/>. A request that matches no endpoint, and one that
/// matches a path under the wrong verb, are asserted at the status the framework produced when this
/// was written; a framework that stopped evaluating the fallback for them would change those
/// answers, and these tests say so.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AuthorizationFallbackEndpointTests : IAsyncLifetime
{
    /// <summary>The route of the probe endpoint that deliberately carries no authorization metadata.</summary>
    private const string ProbePath = "/api/v1/test-probes/unmarked";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AuthorizationFallbackEndpointTests(PostgresFixture postgres)
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

    /// <summary>The probe really is an endpoint with no authorization metadata of any kind.</summary>
    /// <remarks>
    /// The precondition of every probe test below: without it a refusal could come from an attribute
    /// rather than the fallback.
    /// </remarks>
    [Fact]
    public async Task The_probe_endpoint_carries_no_authorization_metadata()
    {
        await using var factory = await BootAsync();

        var probe = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(endpoint =>
            {
                return ("/" + endpoint.RoutePattern.RawText!.TrimStart('/')) == ProbePath;
            });

        Assert.Null(probe.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Null(probe.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Null(probe.Metadata.GetMetadata<AuthorizationPolicy>());
    }

    /// <summary>An anonymous caller of an unmarked endpoint is refused with 401.</summary>
    [Fact]
    public async Task An_endpoint_with_no_authorization_metadata_refuses_an_anonymous_caller()
    {
        await using var factory = await BootAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ProbePath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed-in customer of an unmarked endpoint is refused by authorization itself.</summary>
    /// <remarks>
    /// 403 with an empty body: the middleware refused, no handler produced a problem document.
    /// </remarks>
    [Fact]
    public async Task An_endpoint_with_no_authorization_metadata_refuses_a_signed_in_customer()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        var response = await client.GetAsync(ProbePath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    /// <summary>An administrator is admitted to an unmarked endpoint: the fallback is not deny-all.</summary>
    [Fact]
    public async Task An_endpoint_with_no_authorization_metadata_answers_an_administrator()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await client.GetAsync(ProbePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>A path that matches no endpoint is 401 anonymously and 403 for a customer.</summary>
    [Fact]
    public async Task A_path_that_matches_no_endpoint_refuses_an_anonymous_caller_and_a_customer()
    {
        await using var factory = await BootAsync();
        using var anonymous = factory.CreateClient();
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        var anonymousResponse = await anonymous.GetAsync("/api/v1/no-such-endpoint");
        var customerResponse = await customer.GetAsync("/api/v1/no-such-endpoint");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customerResponse.StatusCode);
    }

    /// <summary>An administrator asking for a path that matches no endpoint is told 404.</summary>
    /// <remarks>The inverse control of the customer 403 above: the fallback admits, routing then says not found.</remarks>
    [Fact]
    public async Task A_path_that_matches_no_endpoint_answers_an_administrator_404()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/no-such-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A real path under the wrong verb is 401 anonymously and 403 for a customer.</summary>
    [Fact]
    public async Task A_wrong_verb_on_a_real_path_refuses_an_anonymous_caller_and_a_customer()
    {
        await using var factory = await BootAsync();
        using var anonymous = factory.CreateClient();
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        var anonymousResponse = await anonymous.DeleteAsync("/api/v1/dashboard");
        var customerResponse = await customer.DeleteAsync("/api/v1/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customerResponse.StatusCode);
    }

    /// <summary>An administrator using the wrong verb on a real path is told 405.</summary>
    /// <remarks>The inverse control of the customer 403 above.</remarks>
    [Fact]
    public async Task A_wrong_verb_on_a_real_path_answers_an_administrator_405()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await client.DeleteAsync("/api/v1/dashboard");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    /// <summary>
    /// Boots a host that also maps the unmarked probe, migrated, with one administrator and one
    /// customer to sign in as.
    /// </summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(
            _pg,
            services =>
            {
                services.AddControllers().AddApplicationPart(typeof(UnmarkedProbeController).Assembly);
            });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", accountId);

        return factory;
    }
}
