using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A booted panel for the API key tests: an administrator, a customer, a clock the test can move, and a
/// proxy peer so each request can name the client address it appears to come from.
/// </summary>
/// <remarks>
/// The peer is loopback, which the panel trusts as its reverse proxy, so the address of a request is the
/// <c>X-Forwarded-For</c> value the test sends. That is how one host answers for several callers without
/// a second factory: the failed-attempt limiter and the address binding both read that one spelling.
/// </remarks>
public sealed class ApiKeyHost : IAsyncDisposable
{
    /// <summary>The route of the one endpoint a key may call without any further scope.</summary>
    public const string SelfRoute = "/api/v1/api-keys/self";

    /// <summary>The management route.</summary>
    public const string ManagementRoute = "/api/v1/api-keys";

    /// <summary>The name of the seeded account the customer owns.</summary>
    public const string AccountName = "acme";

    /// <summary>The name of the seeded customer login.</summary>
    public const string CustomerName = "olive";

    /// <summary>The address a request carries when a test does not care which one it is.</summary>
    public const string DefaultAddress = "203.0.113.50";

    private readonly HttpClient _client;

    /// <summary>Wraps a booted factory.</summary>
    /// <param name="factory">The migrated, seeded host.</param>
    /// <param name="clock">The clock the host runs on.</param>
    /// <param name="accountId">The seeded account's id.</param>
    private ApiKeyHost(WebApplicationFactory<Program> factory, AdvancingClock clock, Guid accountId)
    {
        Factory = factory;
        Clock = clock;
        AccountId = accountId;
        _client = factory.CreateClient();
    }

    /// <summary>The booted host.</summary>
    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>The panel's clock; the test moves it to expire a key or to pass the last-use window.</summary>
    public AdvancingClock Clock { get; }

    /// <summary>The id of the account the customer owns.</summary>
    public Guid AccountId { get; }

    /// <summary>Boots a panel with one administrator ("admin") and one customer owning one account.</summary>
    /// <param name="database">This test's own database.</param>
    /// <returns>The host; the caller disposes it.</returns>
    public static async Task<ApiKeyHost> BootAsync(TestDatabase database)
    {
        var clock = new AdvancingClock();
        var factory = EndpointHarness.CreateFactory(database, services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IStartupFilter>(new RemotePeerStartupFilter(IPAddress.Loopback));
        });

        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, AccountName);
        await EndpointHarness.SeedCustomerAsync(factory, CustomerName, accountId);

        return new ApiKeyHost(factory, clock, accountId);
    }

    /// <summary>Signs the administrator in with the panel's own login.</summary>
    /// <returns>A client carrying the administrator's token; the caller disposes it.</returns>
    public Task<HttpClient> SignInAdministratorAsync()
    {
        return EndpointHarness.SignInAsync(Factory, "admin");
    }

    /// <summary>Creates a key through the management endpoint, as an administrator would.</summary>
    /// <param name="admin">The administrator's client.</param>
    /// <param name="name">The key's name.</param>
    /// <param name="scopes">The scopes to grant; <c>self.read</c> alone when omitted.</param>
    /// <param name="allowedCidrs">The address ranges the key is bound to, or <c>null</c> for none.</param>
    /// <param name="expiresAt">When the key stops working, or <c>null</c> for never.</param>
    /// <returns>The key as its creator saw it.</returns>
    public async Task<ApiKeyIssued> CreateKeyAsync(
        HttpClient admin,
        string name,
        IReadOnlyList<string>? scopes = null,
        IReadOnlyList<string>? allowedCidrs = null,
        DateTimeOffset? expiresAt = null)
    {
        IReadOnlyList<string> granted = scopes ?? ["self.read"];
        var response = await admin.PostAsJsonAsync(
            ManagementRoute,
            new { Name = name, Scopes = granted, AllowedCidrs = allowedCidrs, ExpiresAt = expiresAt });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var key = body.RootElement.GetProperty("key");

        return new ApiKeyIssued(
            key.GetProperty("id").GetGuid(),
            key.GetProperty("prefix").GetString()!,
            body.RootElement.GetProperty("secret").GetString()!);
    }

    /// <summary>Revokes a key through the management endpoint.</summary>
    /// <param name="admin">The administrator's client.</param>
    /// <param name="keyId">The key to revoke.</param>
    /// <returns>Resolves once the panel has answered 204.</returns>
    public async Task RevokeKeyAsync(HttpClient admin, Guid keyId)
    {
        var response = await admin.DeleteAsync($"{ManagementRoute}/{keyId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>Sends one GET as an integration would: a key in its header, from a chosen address.</summary>
    /// <param name="path">The route and query to request.</param>
    /// <param name="key">The value of the key header, or <c>null</c> to send none.</param>
    /// <param name="address">The client address the request appears to come from.</param>
    /// <returns>The panel's answer.</returns>
    public Task<HttpResponseMessage> GetAsync(string path, string? key, string address = DefaultAddress)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", address);
        if (key is not null)
        {
            request.Headers.Add("X-Api-Key", key);
        }

        return _client.SendAsync(request);
    }

    /// <summary>Reads a key's stored row, bypassing the API.</summary>
    /// <param name="keyId">The key's id.</param>
    /// <returns>The row as stored.</returns>
    public async Task<ApiKey> ReadRowAsync(Guid keyId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await context.ApiKeys.AsNoTracking().SingleAsync(key => key.Id == keyId);
    }

    /// <summary>Reads every journal entry of one action, oldest first.</summary>
    /// <param name="action">The audit action name.</param>
    /// <returns>The entries.</returns>
    public async Task<IReadOnlyList<AuditEvent>> ReadAuditAsync(string action)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await context.AuditEvents
            .AsNoTracking()
            .Where(entry => entry.Action == action)
            .OrderBy(entry => entry.OccurredAt)
            .ToListAsync();
    }

    /// <summary>Releases the client and the host.</summary>
    /// <returns>Resolves once both are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await Factory.DisposeAsync();
    }
}
