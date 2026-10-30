using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The paged certificate list (<c>GET /api/v1/certificates</c>) over real HTTP against a real
/// PostgreSQL: the domain search is an <c>ILIKE</c> only PostgreSQL runs, the status and expiring
/// filters are translated to SQL against the host's clock, and the bounds are refused by the validator.
/// </summary>
/// <remarks>
/// The tenant boundary of this list — a customer sees only their own certificates — is
/// <c>CertificatesAuthorizationTests</c>'s; this class asks what the list's own parameters do.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class CertificateListEndpointTests : IAsyncLifetime
{
    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public CertificateListEndpointTests(PostgresFixture postgres)
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

    /// <summary>
    /// A search made of LIKE wildcards matches literally, so it matches nothing, while a fragment of a
    /// domain in another case finds that domain.
    /// </summary>
    [Fact]
    public async Task A_search_matches_the_domain_literally_and_ignoring_case()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, [("shop.example.com", 40, 0), ("blog.example.com", 50, 0)]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var hostile = await GetJsonAsync(admin, "/api/v1/certificates?search=" + Uri.EscapeDataString("%_'"));
        using var anyCase = await GetJsonAsync(admin, "/api/v1/certificates?search=SHOP");

        Assert.Equal(0, hostile.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["shop.example.com"], Domains(anyCase));
    }

    /// <summary>Each status filter answers its own certificates, translated to SQL against the host's clock.</summary>
    /// <param name="status">The status, spelled as the JSON answers spell it.</param>
    /// <param name="expected">The domains in that status, soonest expiry first.</param>
    [Theory]
    [InlineData("valid", new[] { "valid.example.com" })]
    [InlineData("renewalFailing", new[] { "failing.example.com" })]
    [InlineData("expired", new[] { "lapsed.example.com" })]
    public async Task Each_status_filter_answers_its_own_certificates(string status, string[] expected)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, [("valid.example.com", 60, 0), ("failing.example.com", 10, 2), ("lapsed.example.com", -1, 0)]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/certificates?status=" + status);

        Assert.Equal(expected, Domains(page));
    }

    /// <summary>The expiring filter answers what expires within thirty days and nothing already expired.</summary>
    [Fact]
    public async Task The_expiring_filter_answers_what_expires_within_thirty_days_and_nothing_already_expired()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, [("soon.example.com", 5, 0), ("later.example.com", 45, 0), ("lapsed.example.com", -1, 0)]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/certificates?expiring=true");

        Assert.Equal(["soon.example.com"], Domains(page));
    }

    /// <summary>Every sort key orders on PostgreSQL in both directions, soonest expiry being the default.</summary>
    /// <param name="sort">The sort parameter, or empty for the default.</param>
    /// <param name="expected">The domains in that order.</param>
    [Theory]
    [InlineData("", new[] { "c.example.com", "a.example.com", "b.example.com" })]
    [InlineData("notAfter", new[] { "c.example.com", "a.example.com", "b.example.com" })]
    [InlineData("-notAfter", new[] { "b.example.com", "a.example.com", "c.example.com" })]
    [InlineData("domain", new[] { "a.example.com", "b.example.com", "c.example.com" })]
    [InlineData("-domain", new[] { "c.example.com", "b.example.com", "a.example.com" })]
    public async Task Every_sort_key_orders_on_postgresql_in_both_directions(string sort, string[] expected)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, [("a.example.com", 20, 0), ("b.example.com", 90, 0), ("c.example.com", 3, 0)]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, sort.Length == 0 ? "/api/v1/certificates" : "/api/v1/certificates?sort=" + sort);

        Assert.Equal(expected, Domains(page));
    }

    /// <summary>A parameter outside the contract answers 400 with its code.</summary>
    /// <param name="queryString">The refused parameter.</param>
    /// <param name="code">The code it answers.</param>
    [Theory]
    [InlineData("sort=issuedAt", "ListSortUnknown")]
    [InlineData("pageSize=1000", "ListPageSizeNotOffered")]
    public async Task A_parameter_outside_the_contract_answers_400_with_its_code(string queryString, string code)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, [("shop.example.com", 40, 0)]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/certificates?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeOfAsync(response));
    }

    /// <summary>
    /// A status number that names no member is refused with 400 while binding, before the validator's
    /// <c>CertificateStatusUnknown</c> rule is reached: MVC's enum binder refuses an undefined value
    /// itself. The validator rule stays for callers that dispatch the query on the bus without HTTP.
    /// </summary>
    [Fact]
    public async Task A_status_number_naming_no_member_is_refused_while_binding()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, [("shop.example.com", 40, 0)]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/certificates?status=7");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Migrates, seeds the administrator, and stores one certificate per entry on one account, each
    /// expiring the given number of days from the host's clock and failing renewal the given number
    /// of times.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="certificates">Domain, days to expiry and consecutive renewal failures, per certificate.</param>
    private static async Task SeedAsync(
        WebApplicationFactory<Program> factory,
        (string Domain, int DaysToExpiry, int Failures)[] certificates)
    {
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SslDbContext>();
        await context.Database.MigrateAsync();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        foreach (var (domain, daysToExpiry, failures) in certificates)
        {
            var certificate = new Certificate(
                Guid.NewGuid(), accountId, Guid.NewGuid(), domain, CertificateSource.Acme, now.AddDays(daysToExpiry), now.AddDays(-60));
            for (var attempt = 0; attempt < failures; attempt++)
            {
                certificate.RenewalFailed("AcmeValidationFailed", now.AddHours(-attempt - 1));
            }

            context.Certificates.Add(certificate);
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Reads a successful JSON answer.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="path">The path and query string.</param>
    /// <returns>The parsed body.</returns>
    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The domains on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    /// <returns>The domains.</returns>
    private static List<string> Domains(JsonDocument page)
    {
        return page.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => { return item.GetProperty("domain").GetString()!; })
            .ToList();
    }

    /// <summary>Reads the machine code of a problem answer.</summary>
    /// <param name="response">The refused response.</param>
    /// <returns>The code.</returns>
    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString()!;
    }
}
