using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The sign-in-adjacent endpoints a customer uses on their own behalf — listing their sessions and
/// managing their second factor — over real HTTP, signed in as a CUSTOMER.
/// </summary>
/// <remarks>
/// <para>
/// Each of these endpoints names the customer-level policy explicitly now that an endpoint naming
/// nothing is administrator-only. The route-table census sees that a policy is named; only a call
/// as a customer sees that it is the RIGHT one, because an administrator policy written where the
/// customer one was meant is a perfectly valid declaration.
/// </para>
/// <para>
/// Reaching the handler is proved by the handler's own answer. An authorization refusal is a bare
/// 401 or 403 with no body; a handler refusal is a problem document carrying a <c>code</c>. So the
/// confirm and disable tests assert the code, and a policy that refused the customer would fail them
/// on the empty body.
/// </para>
/// <para>
/// UNOBSERVED HERE: the success path of confirming and disabling needs a valid one-time code, which
/// the tests do not compute; only enrolment is followed through to a body.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class CustomerSelfServiceEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public CustomerSelfServiceEndpointTests(PostgresFixture postgres)
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

    /// <summary>A signed-in customer lists their own session, marked as the current one.</summary>
    [Fact]
    public async Task A_signed_in_customer_can_list_their_own_sessions()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var session = Assert.Single(body.RootElement.EnumerateArray());
        Assert.True(session.GetProperty("isCurrent").GetBoolean());
    }

    /// <summary>A signed-in customer can begin a two-factor enrolment and is handed a secret.</summary>
    [Fact]
    public async Task A_signed_in_customer_can_begin_two_factor_enrolment()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        var response = await client.PostAsync("/api/v1/auth/two-factor/enrol", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("secret").GetString()));
    }

    /// <summary>A signed-in customer confirming with a wrong code reaches the handler and is told so.</summary>
    [Fact]
    public async Task A_signed_in_customer_reaches_the_two_factor_confirmation_handler()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "customer");
        var enrolment = await client.PostAsync("/api/v1/auth/two-factor/enrol", content: null);
        using var enrolled = JsonDocument.Parse(await enrolment.Content.ReadAsStringAsync());
        var secret = enrolled.RootElement.GetProperty("secret").GetString();

        var response = await client.PostAsJsonAsync("/api/v1/auth/two-factor/confirm", new { Secret = secret, Code = "000000" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InvalidTwoFactorCodeUnauthorized", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>A signed-in customer disabling a factor they never enabled reaches the handler and is told so.</summary>
    [Fact]
    public async Task A_signed_in_customer_reaches_the_two_factor_disable_handler()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        var response = await client.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { Code = "000000" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("TwoFactorNotEnabledForbidden", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>An anonymous caller is refused by every one of these endpoints.</summary>
    /// <remarks>The inverse control: the customer-level policy still requires a signed-in caller.</remarks>
    [Fact]
    public async Task An_anonymous_caller_is_refused_by_the_self_service_endpoints()
    {
        await using var factory = await BootAsync();
        using var client = factory.CreateClient();

        var sessions = await client.GetAsync("/api/v1/sessions");
        var enrol = await client.PostAsync("/api/v1/auth/two-factor/enrol", content: null);
        var confirm = await client.PostAsJsonAsync("/api/v1/auth/two-factor/confirm", new { Secret = "x", Code = "000000" });
        var disable = await client.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { Code = "000000" });

        Assert.Equal(HttpStatusCode.Unauthorized, sessions.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, enrol.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, disable.StatusCode);
    }

    /// <summary>Boots the host, migrated, with one customer to sign in as.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", accountId);

        return factory;
    }
}
