using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Seeders;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// DNS zone templates over real HTTP against real PostgreSQL: the administrator-only management routes refuse a
/// customer and an anonymous caller, a customer is offered template names and never their records, a zone made
/// from a template holds its records and is invisible to another account, the record cap still applies, and every
/// outcome is journalled. Each refusal has an accepting control.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class DnsZoneTemplatesEndpointTests : IAsyncLifetime
{
    /// <summary>The password every seeded user signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The key the host is configured with for encryption and token signing.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The administrator's template routes, with the method each takes.</summary>
    /// <returns>The method and path template rows.</returns>
    public static TheoryData<string, string> TemplateEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/dns-zone-templates" },
            { "GET", "/api/v1/dns-zone-templates/variables" },
            { "GET", "/api/v1/dns-zone-templates/{id}" },
            { "POST", "/api/v1/dns-zone-templates" },
            { "PUT", "/api/v1/dns-zone-templates/{id}" },
            { "DELETE", "/api/v1/dns-zone-templates/{id}" },
            { "POST", "/api/v1/dns-zone-templates/{id}/make-default" },
        };
    }

    /// <summary>The shared PostgreSQL server this test's database lives on.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public DnsZoneTemplatesEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Creates this test's database.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Nothing to release: the database is dropped with the server.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A customer is refused 403 on every template administration route, whatever it names.</summary>
    [Theory]
    [MemberData(nameof(TemplateEndpoints))]
    public async Task A_customer_is_refused_every_template_administration_route(string method, string path)
    {
        await using var factory = CreateFactory();
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, path.Replace("{id}", world.WebTemplateId.ToString(), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An anonymous caller is refused 401 on every template administration route.</summary>
    [Theory]
    [MemberData(nameof(TemplateEndpoints))]
    public async Task An_anonymous_caller_is_refused_every_template_administration_route(string method, string path)
    {
        await using var factory = CreateFactory();
        var world = await SeedAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, path.Replace("{id}", world.WebTemplateId.ToString(), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer is offered each template's id, name and default flag, and nothing of its records.</summary>
    [Fact]
    public async Task A_customer_is_offered_template_names_and_never_their_records()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/dns-zones/templates/choices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        var choices = body.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, choices.Count);
        Assert.All(choices, choice =>
        {
            Assert.Equal(
                ["id", "isDefault", "name"],
                choice.EnumerateObject().Select(property =>
                {
                    return property.Name;
                }).Order(StringComparer.Ordinal));
        });
        Assert.Equal(
            "web",
            choices.Single(choice =>
            {
                return choice.GetProperty("isDefault").GetBoolean();
            }).GetProperty("name").GetString());
        Assert.DoesNotContain("{ipv4}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("records", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Inverse control for the test above: the administrator reads the same template WITH its records.</summary>
    [Fact]
    public async Task An_administrator_reads_a_template_with_its_records()
    {
        await using var factory = CreateFactory();
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync($"/api/v1/dns-zone-templates/{world.WebTemplateId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var records = body.RootElement.GetProperty("records").EnumerateArray().ToList();
        Assert.Equal(3, records.Count);
        Assert.Equal("{ipv4}", records[0].GetProperty("value").GetString());
        Assert.True(body.RootElement.GetProperty("isBuiltIn").GetBoolean());
        Assert.True(body.RootElement.GetProperty("isDefault").GetBoolean());
    }

    /// <summary>The administrator creates, replaces, defaults and deletes a template, and each step is journalled.</summary>
    [Fact]
    public async Task An_administrator_manages_templates_and_each_step_is_journalled()
    {
        await using var factory = CreateFactory();
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var created = await client.PostAsJsonAsync("/api/v1/dns-zone-templates", Template("Starter", "{ipv4}"));
        var createdId = await IdOfAsync(created);
        var replaced = await client.PutAsJsonAsync($"/api/v1/dns-zone-templates/{createdId}", Template("Starter v2", "{ipv4}"));
        var builtIn = await client.DeleteAsync($"/api/v1/dns-zone-templates/{world.WebAndMailTemplateId}");
        var madeDefault = await client.PostAsync($"/api/v1/dns-zone-templates/{createdId}/make-default", null);
        var defaultDelete = await client.DeleteAsync($"/api/v1/dns-zone-templates/{createdId}");
        var back = await client.PostAsync($"/api/v1/dns-zone-templates/{world.WebTemplateId}/make-default", null);
        var deleted = await client.DeleteAsync($"/api/v1/dns-zone-templates/{createdId}");
        var list = await client.GetAsync("/api/v1/dns-zone-templates?search=web");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/v1/dns-zone-templates/{createdId}", created.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, builtIn.StatusCode);
        Assert.Equal("ZoneTemplateBuiltIn", await CodeOfAsync(builtIn));
        Assert.Equal(HttpStatusCode.NoContent, madeDefault.StatusCode);
        Assert.Equal("ZoneTemplateIsDefault", await CodeOfAsync(defaultDelete));
        Assert.Equal(HttpStatusCode.NoContent, back.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(2, page.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(
            [true, false],
            page.RootElement.GetProperty("items").EnumerateArray().Select(item =>
            {
                return item.GetProperty("isDefault").GetBoolean();
            }));

        var actions = await ActionsAsync(factory);
        Assert.Contains((DnsAuditActions.ZoneTemplateSaved, true), actions);
        Assert.Contains((DnsAuditActions.ZoneTemplateDeleted, true), actions);
        Assert.Contains((DnsAuditActions.ZoneTemplateDeleted, false), actions);
        Assert.Contains((DnsAuditActions.ZoneTemplateDefaultChanged, true), actions);
    }

    /// <summary>An unknown variable is refused 400 with its own code, and a known one is created.</summary>
    [Fact]
    public async Task A_template_with_an_unknown_variable_is_refused()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var refused = await client.PostAsJsonAsync("/api/v1/dns-zone-templates", TextTemplate("Bad", "{nope}"));
        var accepted = await client.PostAsJsonAsync("/api/v1/dns-zone-templates", TextTemplate("Good", "{apex}"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("ZoneTemplateTokenUnknown", await CodeOfAsync(refused));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    /// <summary>The administrator reads the closed catalogue, with its words already in the caller's language.</summary>
    [Fact]
    public async Task The_variable_catalogue_arrives_from_the_server()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/dns-zone-templates/variables");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var variables = body.RootElement.EnumerateArray().ToList();
        Assert.Equal(
            ["{apex}", "{ipv4}", "{ipv6}", "{ns1}", "{ns2}", "{hostmaster}"],
            variables.Select(variable =>
            {
                return variable.GetProperty("token").GetString();
            }));
        Assert.All(variables, variable =>
        {
            Assert.False(variable.GetProperty("description").GetString()!.StartsWith("DnsTemplateVariable", StringComparison.Ordinal));
        });
    }

    /// <summary>A zone made from a template holds its records, is journalled, and is invisible to another account.</summary>
    [Fact]
    public async Task A_zone_made_from_a_template_holds_its_records_and_is_not_visible_to_another_account()
    {
        await using var factory = CreateFactory();
        var world = await SeedAsync(factory);
        using var admin = await SignInAsync(factory, "admin");

        var created = await admin.PostAsJsonAsync(
            "/api/v1/dns-zones",
            new { accountId = world.StrangerAccountId, name = "stranger.example", templateId = world.WebTemplateId });
        var zoneId = await IdOfAsync(created);
        var records = await admin.GetAsync($"/api/v1/dns-zones/{zoneId}/records");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var zone = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(2, zone.RootElement.GetProperty("recordCount").GetInt32());
        Assert.Equal(1, zone.RootElement.GetProperty("skippedTemplateRecords").GetArrayLength());
        using var items = JsonDocument.Parse(await records.Content.ReadAsStringAsync());
        var rows = items.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal("template", row.GetProperty("origin").GetString());
            Assert.True(row.GetProperty("editable").GetBoolean());
        });
        Assert.Contains((DnsAuditActions.DnsZoneCreated, true), await ActionsAsync(factory));

        using var customer = await SignInAsync(factory, "customer");
        var read = await customer.GetAsync($"/api/v1/dns-zones/{zoneId}");
        var readRecords = await customer.GetAsync($"/api/v1/dns-zones/{zoneId}/records");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, readRecords.StatusCode);
    }

    /// <summary>A template that does not exist is 404 with its code, and no zone is created.</summary>
    [Fact]
    public async Task Creating_a_zone_from_an_unknown_template_is_not_found()
    {
        await using var factory = CreateFactory();
        var world = await SeedAsync(factory);
        using var admin = await SignInAsync(factory, "admin");

        var response = await admin.PostAsJsonAsync(
            "/api/v1/dns-zones",
            new { accountId = world.StrangerAccountId, name = "stranger.example", templateId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ZoneTemplateNotFound", await CodeOfAsync(response));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<DnsDbContext>().Zones.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>The server's record cap still applies to a template, at save and at zone creation.</summary>
    [Fact]
    public async Task The_record_cap_applies_to_a_template_at_save_and_at_creation()
    {
        await using var factory = CreateFactory(("Dns:MaxRecordsPerZone", "2"));
        var world = await SeedAsync(factory);
        using var admin = await SignInAsync(factory, "admin");

        var save = await admin.PostAsJsonAsync("/api/v1/dns-zone-templates", new
        {
            name = "Three",
            description = string.Empty,
            records = new[]
            {
                Record("a", "{ipv4}"),
                Record("b", "{ipv4}"),
                Record("c", "{ipv4}"),
            },
        });
        var create = await admin.PostAsJsonAsync(
            "/api/v1/dns-zones",
            new { accountId = world.StrangerAccountId, name = "stranger.example", templateId = world.WebAndMailTemplateId });
        var web = await admin.PostAsJsonAsync(
            "/api/v1/dns-zones",
            new { accountId = world.StrangerAccountId, name = "web.example", templateId = world.WebTemplateId });

        Assert.Equal("ZoneTemplateTooManyRecords", await CodeOfAsync(save));
        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        Assert.Equal("ZoneTemplateTooManyRecords", await CodeOfAsync(create));
        Assert.Equal(HttpStatusCode.Created, web.StatusCode);
    }

    /// <summary>Builds a record body.</summary>
    /// <param name="name">The owner name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The body.</returns>
    private static object Record(string name, string value)
    {
        return new { name, type = "a", ttl = (int?)null, value, priority = 0, weight = 0, port = 0, flags = 0, tag = string.Empty };
    }

    /// <summary>Builds a template body with one A record.</summary>
    /// <param name="name">The template's name.</param>
    /// <param name="value">The A record's value.</param>
    /// <returns>The body.</returns>
    private static object Template(string name, string value)
    {
        return new { name, description = "about", records = new[] { Record("@", value) } };
    }

    /// <summary>Builds a template body with one TXT record.</summary>
    /// <param name="name">The template's name.</param>
    /// <param name="value">The TXT value.</param>
    /// <returns>The body.</returns>
    private static object TextTemplate(string name, string value)
    {
        return new
        {
            name,
            description = string.Empty,
            records = new[]
            {
                new { name = "@", type = "txt", ttl = (int?)null, value, priority = 0, weight = 0, port = 0, flags = 0, tag = string.Empty },
            },
        };
    }

    /// <summary>Reads the <c>id</c> of a response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The identity.</returns>
    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Reads the machine <c>code</c> of a problem response.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The code.</returns>
    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("code").GetString();
    }

    /// <summary>Reads the audit journal's action names and outcomes.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The pairs, in the order they were written.</returns>
    private static async Task<List<(string Action, bool Succeeded)>> ActionsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
            .AsNoTracking()
            .OrderBy(entry => entry.OccurredAt)
            .Select(entry => new { entry.Action, entry.Succeeded })
            .ToListAsync();

        return rows.Select(row =>
        {
            return (row.Action, row.Succeeded);
        }).ToList();
    }

    /// <summary>Builds the host against this test's database with the agent stubbed.</summary>
    /// <param name="extra">Further settings for the host.</param>
    /// <returns>The factory.</returns>
    private WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] extra)
    {
        return new ConfiguredHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            foreach (var (key, value) in extra)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentDnsClient>(new StubAgentDnsClient());
            });
        });
    }

    /// <summary>
    /// Migrates every schema the tests touch, seeds two accounts, an administrator and a customer of the first, the
    /// built-in zone templates through the seeder, and a complete, switched-on set of name server settings with no
    /// IPv6 address.
    /// </summary>
    /// <param name="factory">The host.</param>
    /// <returns>The identifiers the tests aim at.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var dns = scope.ServiceProvider.GetRequiredService<DnsDbContext>();
        await identity.Database.MigrateAsync();
        await accounts.Database.MigrateAsync();
        await dns.Database.MigrateAsync();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5, 3));
        var own = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
        var stranger = new Account(Guid.NewGuid(), "stranger", "stranger.example.com", planId, now);
        accounts.Accounts.AddRange(own, stranger);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        identity.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), customer.Id, own.Id, now));
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<ZoneTemplateSeeder>().SeedAsync(CancellationToken.None);
        var settings = await dns.DnsSettings.SingleAsync();
        settings.Save("ns1.panel.test", "ns2.panel.test", "hostmaster.panel.test", "203.0.113.5", string.Empty, 3600, now);
        settings.Enable(now);
        await dns.SaveChangesAsync();

        return new SeededWorld(stranger.Id, ZoneTemplateSeeder.WebTemplateId, ZoneTemplateSeeder.WebAndMailTemplateId);
    }

    /// <summary>Signs a seeded user in and returns a client carrying the access token.</summary>
    /// <param name="factory">The host.</param>
    /// <param name="username">The seeded user.</param>
    /// <returns>The authenticated client.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Sends one request with a body that is valid enough to reach the handler on a write.</summary>
    /// <param name="client">The client.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" && !path.EndsWith("/make-default", StringComparison.Ordinal))
        {
            request.Content = JsonContent.Create(Template("Probe", "{ipv4}"));
        }

        return await client.SendAsync(request);
    }

    /// <summary>The identities the tests aim at.</summary>
    /// <param name="StrangerAccountId">The account the customer does not belong to.</param>
    /// <param name="WebTemplateId">The built-in web template.</param>
    /// <param name="WebAndMailTemplateId">The built-in web and mail template.</param>
    private sealed record SeededWorld(Guid StrangerAccountId, Guid WebTemplateId, Guid WebAndMailTemplateId);
}
