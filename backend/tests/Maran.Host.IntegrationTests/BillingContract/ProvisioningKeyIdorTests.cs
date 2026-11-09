using System.Net;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>
/// One reseller's key against another reseller's rows on the reseller API (issue #7): every route answers
/// 404 exactly as it does for an id that names nothing, so the key learns nothing about whose account it is,
/// and nothing about the other reseller's account changes.
/// </summary>
/// <remarks>
/// <para>
/// The foreign probes are compared with probes of a random id (status and code), as in
/// <see cref="ResellerIsolationTests"/>, so a 404 that differed from "no such account" would fail. The
/// accepting control is in each test: the same key does the same call on its own account and it works.
/// </para>
/// <para>
/// UNOBSERVED HERE: the administrator's accounts and plans (the house account) are probed in the same way
/// by <see cref="ResellerApiKeyEndpointTests"/> for the reads; the writes here use the other reseller's.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ProvisioningKeyIdorTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ProvisioningKeyIdorTests(PostgresFixture postgres)
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

    /// <summary>The five routes that name an account, as a request that would succeed on one's own.</summary>
    private static IReadOnlyList<(string Name, HttpMethod Method, string Suffix, object? Body)> AccountRoutes(Guid planId)
    {
        return
        [
            ("read", HttpMethod.Get, string.Empty, null),
            ("usage", HttpMethod.Get, "/usage", null),
            ("suspend", HttpMethod.Post, "/suspend", null),
            ("reactivate", HttpMethod.Post, "/reactivate", null),
            ("change plan", HttpMethod.Put, "/plan", new { PlanId = planId }),
            ("terminate", HttpMethod.Delete, string.Empty, null),
        ];
    }

    /// <summary>Sends an account route with a fresh <c>Idempotency-Key</c> on a write.</summary>
    private static Task<HttpResponseMessage> SendAsync(
        BillingContractWorld world,
        (string Name, HttpMethod Method, string Suffix, object? Body) route,
        Guid accountId,
        string secret)
    {
        var key = route.Method == HttpMethod.Get ? null : "idor-" + Guid.NewGuid().ToString("N");

        return world.SendAsync(route.Method, $"/accounts/{accountId}{route.Suffix}", secret, key, route.Body);
    }

    /// <summary>
    /// Alpha's key on bravo's account answers 404 <c>AccountNotFound</c> on every route that names an account,
    /// the same status and code as an id that names nothing, and bravo's account is left exactly as it was.
    /// </summary>
    [Fact]
    public async Task A_key_naming_another_resellers_account_is_answered_as_an_unknown_id_is_on_every_route()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        var foreign = world.World.Bravo.AccountId;

        foreach (var route in AccountRoutes(world.World.Alpha.PlanId))
        {
            using var unknown = await SendAsync(world, route, Guid.NewGuid(), world.Full.Secret);
            using var response = await SendAsync(world, route, foreign, world.Full.Secret);

            var expected = await ProblemDocument.SignatureAsync(unknown);
            Assert.Equal((HttpStatusCode.NotFound, "AccountNotFound"), expected);
            Assert.True(
                expected == await ProblemDocument.SignatureAsync(response),
                $"'{route.Name}': another reseller's account was not answered as an unknown id is.");
        }

        var stored = await world.StoredAccountAsync(foreign);
        Assert.NotNull(stored);
        Assert.Equal(AccountStatus.Active, stored.Status);
        Assert.Equal(world.World.Bravo.PlanId, stored.PlanId);
        Assert.Empty(world.Agent.Deleted);
        Assert.Empty(world.Backup.Creates);
    }

    /// <summary>
    /// The same holds the other way round, and the accepting control is the owner itself: bravo's key reads and
    /// suspends bravo's own account, so the 404 above is a matter of owner and not of the route.
    /// </summary>
    [Fact]
    public async Task Each_resellers_key_reaches_its_own_account_and_not_the_others()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);

        using var ownRead = await world.SendAsync(HttpMethod.Get, $"/accounts/{world.World.Bravo.AccountId}", world.BravoFull.Secret);
        using var ownSuspend = await world.SendAsync(
            HttpMethod.Post, $"/accounts/{world.World.Bravo.AccountId}/suspend", world.BravoFull.Secret, "idor-own");
        using var crossRead = await world.SendAsync(HttpMethod.Get, $"/accounts/{world.World.Alpha.AccountId}", world.BravoFull.Secret);
        using var crossSuspend = await world.SendAsync(
            HttpMethod.Post, $"/accounts/{world.World.Alpha.AccountId}/suspend", world.BravoFull.Secret, "idor-cross");

        Assert.Equal(HttpStatusCode.OK, ownRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownSuspend.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossSuspend.StatusCode);
        var alpha = await world.StoredAccountAsync(world.World.Alpha.AccountId);
        var bravo = await world.StoredAccountAsync(world.World.Bravo.AccountId);
        Assert.Equal(AccountStatus.Active, alpha!.Status);
        Assert.Equal(AccountStatus.Suspended, bravo!.Status);
    }

    /// <summary>
    /// A plan of another reseller is answered 404 <c>PlanNotFound</c> as an unknown plan is, on a creation and on
    /// a plan change, and nothing is created or moved.
    /// </summary>
    [Fact]
    public async Task A_plan_of_another_reseller_is_answered_as_an_unknown_plan_is()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        var account = $"/accounts/{world.World.Alpha.AccountId}";

        object Create(string name, Guid plan)
        {
            return new { Name = name, PrimaryDomain = $"{name}.example.com", PlanId = plan, OwnerEmail = "owner@example.com" };
        }

        using var unknownCreate = await world.SendAsync(
            HttpMethod.Post, "/accounts", world.Full.Secret, "idor-plan-1", Create("idorone", Guid.NewGuid()));
        using var foreignCreate = await world.SendAsync(
            HttpMethod.Post, "/accounts", world.Full.Secret, "idor-plan-2", Create("idortwo", world.World.Bravo.PlanId));
        using var unknownMove = await world.SendAsync(
            HttpMethod.Put, account + "/plan", world.Full.Secret, "idor-plan-3", new { PlanId = Guid.NewGuid() });
        using var foreignMove = await world.SendAsync(
            HttpMethod.Put, account + "/plan", world.Full.Secret, "idor-plan-4", new { PlanId = world.World.Bravo.PlanId });

        Assert.Equal((HttpStatusCode.NotFound, "PlanNotFound"), await ProblemDocument.SignatureAsync(unknownCreate));
        Assert.Equal(await ProblemDocument.SignatureAsync(unknownCreate), await ProblemDocument.SignatureAsync(foreignCreate));
        Assert.Equal((HttpStatusCode.NotFound, "PlanNotFound"), await ProblemDocument.SignatureAsync(unknownMove));
        Assert.Equal(await ProblemDocument.SignatureAsync(unknownMove), await ProblemDocument.SignatureAsync(foreignMove));
        Assert.Empty(world.Agent.Created);
        Assert.Equal(world.World.Alpha.PlanId, (await world.StoredAccountAsync(world.World.Alpha.AccountId))!.PlanId);
    }

    /// <summary>
    /// An account created by a key is owned by the key's reseller whatever the body says: naming itself changes
    /// nothing, and naming another reseller is answered as a reseller that does not exist and creates nothing.
    /// </summary>
    [Fact]
    public async Task A_created_account_belongs_to_the_keys_reseller_and_a_body_cannot_name_another()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);

        object Create(string name, Guid? reseller)
        {
            return new
            {
                Name = name,
                PrimaryDomain = $"{name}.example.com",
                PlanId = world.World.Alpha.PlanId,
                OwnerEmail = "owner@example.com",
                ResellerId = reseller,
            };
        }

        using var plain = await world.SendAsync(HttpMethod.Post, "/accounts", world.Full.Secret, "owner-1", Create("ownerone", null));
        using var naming = await world.SendAsync(
            HttpMethod.Post, "/accounts", world.Provisioner.Secret, "owner-2", Create("ownertwo", world.World.Bravo.ResellerId));

        Assert.Equal(HttpStatusCode.Created, plain.StatusCode);
        var created = await world.World.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().SingleAsync(account => account.Name == "ownerone");
        });
        Assert.Equal(world.World.Alpha.ResellerId, created.ResellerId);
        Assert.Equal((HttpStatusCode.NotFound, "ResellerNotFound"), await ProblemDocument.SignatureAsync(naming));
        Assert.Null(await world.World.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().SingleOrDefaultAsync(account => account.Name == "ownertwo");
        }));
        Assert.DoesNotContain("ownertwo", world.Agent.Created);
    }
}
