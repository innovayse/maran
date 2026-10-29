using System.Net;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>POST /api/v1/auth/logout-all</c> over real HTTP, seen from the audit journal: a signed-in
/// customer reaching the handler leaves exactly one entry, and a caller the authorization layer
/// refuses leaves none.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint names its policy explicitly now that the fallback is administrator-only; the
/// customer test proves the handler still runs for a customer (the entry exists only if it did),
/// and the anonymous test is the inverse control that an entry is not written by the refusal
/// itself.
/// </para>
/// <para>
/// UNOBSERVED HERE: authorization refusals happen in middleware before any handler and the panel
/// journals none of them, so there is no entry to assert for the fallback's own refusals.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class LogoutEverywhereAuditTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public LogoutEverywhereAuditTests(PostgresFixture postgres)
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

    /// <summary>A signed-in customer signing out everywhere is journalled as that customer's success.</summary>
    /// <remarks>
    /// Definition of Done item 4. The entry also proves the explicit customer-level policy on the
    /// action admits a customer: a refusal in the authorization layer would never reach the handler
    /// that writes it.
    /// </remarks>
    [Fact]
    public async Task A_customer_signing_out_everywhere_is_journalled_as_their_success()
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

        var response = await client.PostAsync("/api/v1/auth/logout-all", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var userId = await FindUserIdAsync(factory, "customer");
        var entry = Assert.Single(audit.Entries, candidate =>
        {
            return candidate.Action == AuditActions.LoggedOutEverywhere;
        });
        Assert.Equal(userId, entry.ActorUserId);
        Assert.Equal(userId.ToString(), entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An anonymous caller is refused and nothing is journalled as a sign-out.</summary>
    [Fact]
    public async Task An_anonymous_caller_signing_out_everywhere_leaves_no_sign_out_entry()
    {
        var audit = new RecordingAuditWriter();
        await using var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton<IAuditWriter>(audit);
        });
        await EndpointHarness.MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/logout-all", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.LoggedOutEverywhere;
        });
    }

    /// <summary>Looks up the id of a seeded login.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login name.</param>
    /// <returns>The user's id.</returns>
    private static async Task<Guid> FindUserIdAsync(
        WebApplicationFactory<Program> factory,
        string username)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await context.Users.Where(user => user.Username == username).Select(user => user.Id).SingleAsync();
    }
}
