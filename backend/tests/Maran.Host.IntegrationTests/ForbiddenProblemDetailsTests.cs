using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What a signed-in customer reads when a policy refuses them: a localized RFC 7807 body with a
/// machine code, the same for a real administrator route and for a route that does not exist.
/// </summary>
/// <remarks>
/// The unmatched route is the control that matters: the fallback policy refuses a customer there
/// too, so a body that named the administrator role would tell a customer which routes exist.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ForbiddenProblemDetailsTests : IAsyncLifetime
{
    /// <summary>The administrator-only routes a customer is refused on.</summary>
    private static readonly string[] AdminRoutes =
    [
        "/api/v1/audit",
        "/api/v1/notifications/smtp",
        "/api/v1/accounts",
        "/api/v1/security-policy",
    ];

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ForbiddenProblemDetailsTests(PostgresFixture postgres)
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

    /// <summary>A customer on an administrator route gets a 403 problem body with a code and a title.</summary>
    [Fact]
    public async Task A_customer_on_an_administrator_route_gets_a_403_problem_body_with_a_code_and_a_title()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        foreach (var route in AdminRoutes)
        {
            using var response = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("HostForbidden", body.RootElement.GetProperty("code").GetString());
            Assert.Equal("You do not have access to this page.", body.RootElement.GetProperty("title").GetString());
        }
    }

    /// <summary>The refusal is written in the caller's language.</summary>
    [Theory]
    [InlineData("ru", "У вас нет доступа к этой странице.")]
    [InlineData("hy", "Դուք այս էջի հասանելիություն չունեք։")]
    public async Task The_refusal_is_written_in_the_callers_language(string language, string expected)
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "customer");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));

        using var response = await client.GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement.GetProperty("title").GetString());
    }

    /// <summary>An unmatched route answers the same code and text as a real administrator route.</summary>
    [Fact]
    public async Task An_unmatched_route_answers_the_same_code_and_text_as_a_real_administrator_route()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        using var real = await client.GetAsync("/api/v1/audit");
        using var missing = await client.GetAsync("/api/v1/no-such-endpoint");

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        using var realBody = JsonDocument.Parse(await real.Content.ReadAsStringAsync());
        using var missingBody = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Equal(Text(realBody, "code"), Text(missingBody, "code"));
        Assert.Equal(Text(realBody, "title"), Text(missingBody, "title"));
        Assert.Equal(Text(realBody, "detail"), Text(missingBody, "detail"));
    }

    /// <summary>An anonymous caller is still challenged with 401, not given the 403 body.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_still_challenged_with_401()
    {
        await using var factory = await BootAsync();
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Reads one string member of a problem body.</summary>
    /// <param name="body">The parsed body.</param>
    /// <param name="member">The member name.</param>
    private static string? Text(JsonDocument body, string member)
    {
        return body.RootElement.GetProperty(member).GetString();
    }

    /// <summary>Boots a migrated host with one administrator and one customer.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", accountId);
        return factory;
    }
}
