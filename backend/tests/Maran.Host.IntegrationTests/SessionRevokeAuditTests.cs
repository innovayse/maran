using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>DELETE /api/v1/sessions/{id}</c> over real HTTP, seen from the audit journal, for a
/// customer: the endpoint names its customer-level policy explicitly now that the fallback is
/// administrator-only, so a customer revoking their own session must still reach the handler and
/// leave one entry, and revoking somebody else's must leave none.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class SessionRevokeAuditTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SessionRevokeAuditTests(PostgresFixture postgres)
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

    /// <summary>A customer ending their own session is journalled as that customer's success.</summary>
    /// <remarks>Definition of Done item 4; the entry exists only if the explicit policy admitted the customer.</remarks>
    [Fact]
    public async Task A_customer_revoking_their_own_session_is_journalled_as_their_success()
    {
        var audit = new RecordingAuditWriter();
        await using var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton<IAuditWriter>(audit);
        });
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", accountId);
        using var client = await EndpointHarness.SignInAsync(factory, "customer");
        var sessionId = await OnlySessionIdAsync(client);

        var response = await client.DeleteAsync($"/api/v1/sessions/{sessionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(audit.Entries, candidate =>
        {
            return candidate.Action == AuditActions.SessionRevoked;
        });
        Assert.Equal(sessionId, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A customer naming another customer's session gets 404 and nothing is journalled as a revocation.</summary>
    [Fact]
    public async Task A_customer_revoking_another_customers_session_answers_404_and_leaves_no_entry()
    {
        var audit = new RecordingAuditWriter();
        await using var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton<IAuditWriter>(audit);
        });
        await EndpointHarness.MigrateAsync(factory);
        var (_, aliceAccountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, bobAccountId) = await EndpointHarness.SeedAccountAsync(factory, "globex");
        await EndpointHarness.SeedCustomerAsync(factory, "alice", aliceAccountId);
        await EndpointHarness.SeedCustomerAsync(factory, "bob", bobAccountId);
        using var alice = await EndpointHarness.SignInAsync(factory, "alice");
        using var bob = await EndpointHarness.SignInAsync(factory, "bob");
        var bobSessionId = await OnlySessionIdAsync(bob);

        var response = await alice.DeleteAsync($"/api/v1/sessions/{bobSessionId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.SessionRevoked;
        });
    }

    /// <summary>Reads the single session the caller has.</summary>
    /// <param name="client">A signed-in client.</param>
    /// <returns>The session id as text.</returns>
    private static async Task<string> OnlySessionIdAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/sessions"));

        return body.RootElement.EnumerateArray().Single().GetProperty("id").GetString()!;
    }
}
