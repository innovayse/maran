using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.DemoIntegrationTests.Fixtures;

namespace Maran.Host.DemoIntegrationTests;

/// <summary>
/// Over HTTP, a write carrying a blocked term or an outside link is refused with the localized problem and is not
/// stored, a clean write is not stopped by the filter, and the status endpoint reports the abuse contact.
/// </summary>
[Collection(DemoSharedDatabase.Name)]
public sealed class DemoContentEndpointTests : IClassFixture<SharedSeededDemo>
{
    /// <summary>The plan route: a screened action whose command has a text member to refuse on.</summary>
    private const string PlansRoute = "/api/v1/plans";

    /// <summary>The seeded host every test here shares.</summary>
    private readonly SeededDemo _demo;

    /// <summary>The administrator's client, signed in once.</summary>
    private readonly HttpClient _client;

    /// <summary>Creates the class over the shared host.</summary>
    /// <param name="shared">The shared, seeded host.</param>
    public DemoContentEndpointTests(SharedSeededDemo shared)
    {
        _demo = shared.Demo;
        _client = shared.Client;
    }

    /// <summary>Posts a plan with the given name and every other member stated, so only the name can be refused.</summary>
    /// <param name="name">The plan's name.</param>
    /// <param name="language">The Accept-Language value.</param>
    /// <returns>The response.</returns>
    private async Task<HttpResponseMessage> PostPlanAsync(string name, string language = "en")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PlansRoute)
        {
            Content = JsonContent.Create(new
            {
                Name = name,
                DiskQuotaMb = 5_120,
                MaxSites = 5,
                MaxDatabases = 2,
                MaxSftpUsers = 3,
                MaxFtpUsers = 1,
                MaxCronEntries = 2,
                MaxPhpWorkersPerPool = 4,
                CpuQuotaPercent = new { Kind = "limited", Maximum = 100 },
                MemoryMaxMb = new { Kind = "limited", Maximum = 512 },
                MaxProcesses = new { Kind = "limited", Maximum = 64 },
                Features = Array.Empty<string>(),
            }),
        };
        request.Headers.AcceptLanguage.ParseAdd(language);
        return await _client.SendAsync(request);
    }

    /// <summary>A blocked term is answered 400 with the code and the member, and the plan is not stored.</summary>
    [Fact]
    public async Task A_plan_named_with_a_blocked_term_is_refused_and_not_stored()
    {
        var response = await PostPlanAsync("Casino deluxe");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DemoContentRefused", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("Name", body.RootElement.GetProperty("content").GetProperty("field").GetString());
        Assert.DoesNotContain("Casino deluxe", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("Casino deluxe", await _client.GetStringAsync(PlansRoute + "?pageSize=100"));
    }

    /// <summary>The refusal is in the caller's language: the three sentences differ.</summary>
    [Fact]
    public async Task The_refusal_is_localized_in_every_language()
    {
        var titles = new Dictionary<string, string>();
        foreach (var language in new[] { "en", "ru", "hy" })
        {
            var response = await PostPlanAsync("Casino deluxe", language);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            titles[language] = body.RootElement.GetProperty("title").GetString()!;
        }

        Assert.Equal(3, titles.Values.Distinct().Count());
        Assert.DoesNotContain("DemoContentRefused", titles.Values);
    }

    /// <summary>An outside link is refused as well.</summary>
    [Fact]
    public async Task A_plan_named_with_an_outside_link_is_refused()
    {
        var response = await PostPlanAsync("see http://evil.com/offer");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DemoContentRefused", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// The accepting control: a clean name (with a link to an allowed host) is not stopped by the filter. The
    /// body is otherwise incomplete, so the plan's own validator answers; the point is that its code is not ours.
    /// </summary>
    [Fact]
    public async Task A_clean_name_passes_the_filter_and_reaches_the_handler()
    {
        var response = await PostPlanAsync("Starter, see https://example.com/starter");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var code = body.RootElement.TryGetProperty("code", out var found) ? found.GetString() : null;
        Assert.NotEqual("DemoContentRefused", code);
    }

    /// <summary>The status endpoint reports the abuse contact the host was configured with, anonymously.</summary>
    [Fact]
    public async Task The_status_reports_the_configured_abuse_contact()
    {
        using var anonymous = _demo.Factory.CreateClient();

        using var body = JsonDocument.Parse(await anonymous.GetStringAsync("/api/v1/demo/status"));

        Assert.Equal(SeededDemo.AbuseContact, body.RootElement.GetProperty("abuseContact").GetString());
    }
}
