using System.Net;
using Maran.Host.IntegrationTests.Fixtures;

namespace Maran.Host.IntegrationTests.ContractSnapshots;

/// <summary>
/// The response shapes of the account endpoints tagged <c>[Idempotent]</c>, compared with the golden JSON
/// files beside this class (issue #136): the body of a creation, a suspension, a reactivation, a plan
/// change and a deletion, and the problem document of a name conflict, a missing account and a reused
/// idempotency key. A renamed, added, removed or retyped field fails the test with the lines that moved;
/// ids, timestamps and per-request text are normalised away, so only the contract is compared.
/// </summary>
/// <remarks>
/// <b>Blind spots, stated.</b> Response headers (<c>Location</c>), the localized wording of a problem's
/// title and detail, and any answer of a status other than the seven here are not compared. The request
/// bodies are guarded separately by <c>maran api</c>.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AccountResponseContractTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountResponseContractTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// The success bodies of creation, suspension, reactivation, plan change and deletion match their goldens.
    /// </summary>
    [Fact]
    public async Task The_success_responses_of_the_account_lifecycle_match_their_goldens()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (planId, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        var small = await IdempotencyHost.SeedSmallPlanAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var failures = new List<string?>();

        var created = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, "/api/v1/accounts", null, IdempotencyHost.CreateBody("acme", planId)));
        failures.Add(await CheckAsync("accounts-create-201", created, HttpStatusCode.Created));

        var suspended = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, $"/api/v1/accounts/{accountId}/suspend", null));
        failures.Add(await CheckAsync("accounts-suspend-200", suspended, HttpStatusCode.OK));

        var reactivated = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, $"/api/v1/accounts/{accountId}/reactivate", null));
        failures.Add(await CheckAsync("accounts-reactivate-200", reactivated, HttpStatusCode.OK));

        var moved = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Put, $"/api/v1/accounts/{accountId}/plan", null, new { PlanId = small }));
        failures.Add(await CheckAsync("accounts-plan-change-200", moved, HttpStatusCode.OK));

        var deleted = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Delete, $"/api/v1/accounts/{accountId}", null));
        failures.Add(await CheckAsync("accounts-delete-200", deleted, HttpStatusCode.OK));

        AssertNoFailures(failures);
    }

    /// <summary>
    /// The problem documents of a taken name, a missing account and a reused idempotency key match their goldens.
    /// </summary>
    [Fact]
    public async Task The_error_responses_of_the_account_endpoints_match_their_goldens()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (planId, _) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var failures = new List<string?>();

        using var first = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, "/api/v1/accounts", "key-1", IdempotencyHost.CreateBody("acme", planId)));
        var taken = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, "/api/v1/accounts", null, IdempotencyHost.CreateBody("acme", planId)));
        failures.Add(await CheckAsync("accounts-create-409-name-taken", taken, HttpStatusCode.Conflict));

        var missing = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, $"/api/v1/accounts/{Guid.NewGuid()}/suspend", null));
        failures.Add(await CheckAsync("accounts-not-found-404", missing, HttpStatusCode.NotFound));

        var reused = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, "/api/v1/accounts", "key-1", IdempotencyHost.CreateBody("beta", planId)));
        failures.Add(await CheckAsync("idempotency-key-reused-422", reused, HttpStatusCode.UnprocessableEntity));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        AssertNoFailures(failures);
    }

    /// <summary>Compares one response with its golden after checking its status.</summary>
    /// <param name="golden">The golden's name.</param>
    /// <param name="response">The response; disposed here.</param>
    /// <param name="expectedStatus">The status the scenario expects, checked first so a wrong status is named as such.</param>
    /// <returns>Null when it matches; otherwise the description of the mismatch.</returns>
    private static async Task<string?> CheckAsync(string golden, HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != expectedStatus)
            {
                return $"{golden}: expected {(int)expectedStatus} but the server answered {(int)response.StatusCode}: {body}";
            }

            return ContractGolden.Verify(golden, body);
        }
    }

    /// <summary>Fails with every mismatch at once, so one run shows all that moved.</summary>
    /// <param name="failures">One entry per comparison; null for a match.</param>
    private static void AssertNoFailures(IEnumerable<string?> failures)
    {
        var moved = failures.Where(failure => failure is not null).ToList();

        Assert.True(moved.Count == 0, string.Join("\n\n", moved));
    }
}
