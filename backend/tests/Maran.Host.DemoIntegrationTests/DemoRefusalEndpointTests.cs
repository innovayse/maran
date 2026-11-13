using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.DemoIntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.DemoIntegrationTests;

/// <summary>
/// Each class of action the demo refuses is refused with the localized problem AND changes nothing: the
/// published password still signs in, the session survives, and the security policy is byte for byte the same.
/// </summary>
[Collection(DemoSharedDatabase.Name)]
public sealed class DemoRefusalEndpointTests : IClassFixture<SharedSeededDemo>
{
    /// <summary>The seeded host every test here shares; none of them changes it.</summary>
    private readonly SeededDemo _demo;

    /// <summary>The administrator's client, signed in once.</summary>
    private readonly HttpClient _client;

    /// <summary>Creates the class over the shared host.</summary>
    /// <param name="shared">The shared, seeded host.</param>
    public DemoRefusalEndpointTests(SharedSeededDemo shared)
    {
        _demo = shared.Demo;
        _client = shared.Client;
    }

    /// <summary>One refused request and the area that must explain it.</summary>
    /// <returns>Theory rows: method, route, expected area id.</returns>
    public static TheoryData<string, string, string> RefusedRequests()
    {
        var anyId = Guid.NewGuid();
        return new TheoryData<string, string, string>
        {
            { "POST", "/api/v1/auth/change-password", "credentials" },
            { "POST", "/api/v1/auth/logout-all", "credentials" },
            { "DELETE", $"/api/v1/sessions/{anyId}", "credentials" },
            { "POST", "/api/v1/auth/two-factor/enrol", "twoFactor" },
            { "POST", "/api/v1/auth/two-factor/disable", "twoFactor" },
            { "PUT", "/api/v1/security-policy", "securityPolicy" },
            { "POST", "/api/v1/firewall/whitelist", "securityPolicy" },
            { "POST", "/api/v1/notifications/smtp/test", "smtp" },
            { "PUT", "/api/v1/notifications/smtp", "smtp" },
            { "POST", "/api/v1/licence-status/install", "licence" },
            { "POST", "/api/v1/api-keys", "apiKeys" },
        };
    }

    /// <summary>A refused action answers 403 with the code, the area and a sentence.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="route">The route.</param>
    /// <param name="area">The area id the refusal must name.</param>
    [Theory]
    [MemberData(nameof(RefusedRequests))]
    public async Task A_refused_action_is_answered_403_with_its_area(string method, string route, string area)
    {

        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { }),
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DemoActionRefused", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(area, body.RootElement.GetProperty("refusal").GetProperty("area").GetString());
        Assert.False(
            string.IsNullOrWhiteSpace(body.RootElement.GetProperty("refusal").GetProperty("reason").GetString()));
    }

    /// <summary>After every refusal the shared login still works and nothing it guards has changed.</summary>
    [Fact]
    public async Task Refused_actions_leave_the_login_the_session_and_the_policy_untouched()
    {
        var policyBefore = await _client.GetStringAsync("/api/v1/security-policy");

        foreach (var row in RefusedRequests())
        {
            var request = new HttpRequestMessage(new HttpMethod((string)row[0]), (string)row[1])
            {
                Content = JsonContent.Create(new { }),
            };
            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(policyBefore, await _client.GetStringAsync("/api/v1/security-policy"));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/sessions")).StatusCode);
        using var again = await _demo.SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/v1/sessions")).StatusCode);
    }

    /// <summary>
    /// Mail-sending flows are refused, and the capturing mailer is the one registered and holds nothing.
    /// </summary>
    [Fact]
    public async Task No_mail_flow_runs_and_the_mailer_is_the_capturing_one()
    {
        using var anonymous = _demo.Factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Maran-Request", "1");

        var reset = await anonymous.PostAsJsonAsync("/api/v1/auth/forgot-password", new { Username = "nobody" });

        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
        using var scope = _demo.Factory.Services.CreateScope();
        var mailer = scope.ServiceProvider.GetRequiredService<Maran.Modules.Notifications.Interfaces.IMailer>();
        var capturing = Assert.IsType<Maran.Host.Demo.Adapters.CapturingMailer>(mailer);
        Assert.Empty(capturing.Captured);
    }

    /// <summary>The ACME client, the licence verifier and the licence service client are the demo's, not the modules' real ones.</summary>
    [Fact]
    public Task The_acme_client_and_the_licence_verifier_are_the_simulated_ones()
    {
        using var scope = _demo.Factory.Services.CreateScope();

        Assert.IsType<Maran.Host.Demo.Adapters.SimulatedAcmeClient>(
            scope.ServiceProvider.GetRequiredService<Maran.Modules.Ssl.Interfaces.IAcmeClient>());
        Assert.IsType<Maran.Host.Demo.Adapters.DemoLicenceSignatureVerifier>(
            scope.ServiceProvider.GetRequiredService<Maran.Modules.Licensing.Interfaces.ILicenceSignatureVerifier>());
        Assert.IsType<Maran.Host.Demo.Adapters.DemoLeaseServiceClient>(
            scope.ServiceProvider.GetRequiredService<Maran.Modules.Licensing.Interfaces.ILeaseServiceClient>());
        return Task.CompletedTask;
    }

    /// <summary>
    /// The licence is read from and written to memory, never a file: the module's file-backed source and writer
    /// were registered AFTER the guard once, and the last registration wins (F-1 of the security review).
    /// </summary>
    [Fact]
    public void The_licence_is_kept_in_memory_and_not_in_a_file()
    {
        using var scope = _demo.Factory.Services.CreateScope();

        Assert.IsType<Maran.Host.Demo.Adapters.DemoLicenceStore>(
            scope.ServiceProvider.GetRequiredService<Maran.Modules.Licensing.Interfaces.ILicenceRawTextSource>());
        Assert.IsType<Maran.Host.Demo.Adapters.DemoLicenceStore>(
            scope.ServiceProvider.GetRequiredService<Maran.Modules.Licensing.Interfaces.ILicenceWriter>());
        Assert.IsType<Maran.Host.Demo.Adapters.DemoLicenceExpiryAnnouncementStore>(
            scope.ServiceProvider.GetRequiredService<Maran.Modules.Licensing.Interfaces.ILicenceExpiryAnnouncementStore>());
    }

    /// <summary>
    /// The seed protected the resources that hold the demo logins, and the guard refuses to delete one.
    /// </summary>
    [Fact]
    public async Task A_protected_resource_cannot_be_deleted()
    {
        var protectedResources = _demo.Factory.Services
            .GetRequiredService<Maran.Host.Demo.Guard.DemoProtectedResources>();

        Assert.True(protectedResources.Count > 0, "The seed protected nothing, so the guard guards nothing.");
        var response = await _client.DeleteAsync($"/api/v1/accounts/{protectedResources.Ids.First()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("protectedResource", body.RootElement.GetProperty("refusal").GetProperty("area").GetString());
    }
}
