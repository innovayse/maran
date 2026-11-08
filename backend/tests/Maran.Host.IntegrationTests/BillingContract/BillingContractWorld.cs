using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>
/// The panel a billing provider would provision on: the two seeded resellers of <see cref="ResellerWorld"/>,
/// the first (<c>alpha</c>) with a disk pool that holds exactly two of its plans, two more plans of its own,
/// and three keys of its own, plus one key of the second reseller (<c>bravo</c>).
/// </summary>
/// <remarks>
/// <para>
/// Alpha is sold a pool of 10 GiB and owns one account on a 5 GiB plan, so one more account of that plan
/// fits and the next does not; that is what lets the contract meet <c>ResellerDiskPoolExceeded</c> on a
/// creation and on a plan change without a second world. Every module's schema exists, the agent doubles
/// answer a suspension, a usage read and a final backup, and a default backup destination is stored, so
/// every step of the contract meets the real handlers.
/// </para>
/// <para>
/// UNOBSERVED HERE: the host. The agent is a double that records and answers; what the real agent does to a
/// machine is settled by its own polygon suites.
/// </para>
/// </remarks>
public sealed class BillingContractWorld : IAsyncDisposable
{
    /// <summary>The route prefix of the reseller API.</summary>
    public const string ProvisioningRoute = "/api/v1/reseller-api";

    /// <summary>The route a signed-in reseller creates its keys at.</summary>
    private const string KeysRoute = "/api/v1/reseller/api-keys";

    /// <summary>The disk pool alpha is sold: two of its 5 GiB plans.</summary>
    private const long AlphaDiskPoolMb = 10_240;

    /// <summary>The disk, in bytes, the host reports when an account is over the small plan's quota: 3 GiB.</summary>
    private const ulong OverUsageBytes = 3UL * 1024 * 1024 * 1024;

    /// <summary>The failure code the final backup is made to fail with.</summary>
    private const string BackupFailureCode = "AgentSystemFailure";

    /// <summary>The client every request of this world goes through.</summary>
    private readonly HttpClient _client;

    /// <summary>The seeded panel.</summary>
    public ResellerWorld World { get; }

    /// <summary>The account agent double; its <see cref="ResellerAgentAccountsClient.UsedBytes"/> is what the host measures.</summary>
    public ResellerAgentAccountsClient Agent { get; }

    /// <summary>The backup agent double; setting its failure code makes the final backup fail.</summary>
    public StubAgentBackupClient Backup { get; }

    /// <summary>The id of alpha's plan of 2 GiB.</summary>
    public Guid SmallPlanId { get; }

    /// <summary>The id of alpha's plan of 10 GiB, which does not fit alpha's pool beside another account.</summary>
    public Guid LargePlanId { get; }

    /// <summary>A key of alpha holding <c>accounts.read</c>, <c>accounts.write</c> and <c>accounts.delete</c>.</summary>
    public ApiKeyIssued Full { get; }

    /// <summary>A key of alpha holding <c>accounts.read</c> and <c>accounts.write</c>.</summary>
    public ApiKeyIssued Provisioner { get; }

    /// <summary>A key of alpha holding <c>accounts.read</c> only.</summary>
    public ApiKeyIssued Reader { get; }

    /// <summary>A key of bravo holding all three account scopes.</summary>
    public ApiKeyIssued BravoFull { get; }

    /// <summary>Wraps a seeded panel and the keys issued on it.</summary>
    /// <param name="world">The seeded panel.</param>
    /// <param name="agent">The account agent double.</param>
    /// <param name="backup">The backup agent double.</param>
    /// <param name="smallPlanId">Alpha's small plan.</param>
    /// <param name="largePlanId">Alpha's large plan.</param>
    /// <param name="keys">Alpha's three keys, then bravo's.</param>
    private BillingContractWorld(
        ResellerWorld world,
        ResellerAgentAccountsClient agent,
        StubAgentBackupClient backup,
        Guid smallPlanId,
        Guid largePlanId,
        IReadOnlyList<ApiKeyIssued> keys)
    {
        World = world;
        Agent = agent;
        Backup = backup;
        SmallPlanId = smallPlanId;
        LargePlanId = largePlanId;
        Full = keys[0];
        Provisioner = keys[1];
        Reader = keys[2];
        BravoFull = keys[3];
        _client = world.Factory.CreateClient();
    }

    /// <summary>Boots the panel, seeds the resellers and plans, and issues the keys.</summary>
    /// <param name="database">This test's own database.</param>
    /// <returns>The world, which the caller disposes.</returns>
    public static async Task<BillingContractWorld> BootAsync(TestDatabase database)
    {
        var agent = new ResellerAgentAccountsClient();
        var backup = new StubAgentBackupClient();
        var world = await new ResellerFixtureBuilder(database)
            .WithAgent(agent)
            .WithAccountLifecycle(backup)
            .WithAlphaEnvelope(new ResellerEnvelopeSpec(DiskPoolMb: AlphaDiskPoolMb))
            .BuildAsync();

        var small = Guid.NewGuid();
        var large = Guid.NewGuid();
        await world.AccountsAsync(async accounts =>
        {
            accounts.Plans.Add(Plan.Define(
                small, "AlphaSmall", 2_048, 2, 1, 1, 0, 0, 4, ResellerEnvelopeSpec.SoldFeatures, world.Alpha.ResellerId));
            accounts.Plans.Add(Plan.Define(
                large, "AlphaLarge", 10_240, 2, 1, 1, 0, 0, 4, ResellerEnvelopeSpec.SoldFeatures, world.Alpha.ResellerId));
            await accounts.SaveChangesAsync();
            return 0;
        });

        var keys = new List<ApiKeyIssued>
        {
            await IssueKeyAsync(world, "alpha", "full", ApiKeyScopes.AccountsRead, ApiKeyScopes.AccountsWrite, ApiKeyScopes.AccountsDelete),
            await IssueKeyAsync(world, "alpha", "provisioner", ApiKeyScopes.AccountsRead, ApiKeyScopes.AccountsWrite),
            await IssueKeyAsync(world, "alpha", "reader", ApiKeyScopes.AccountsRead),
            await IssueKeyAsync(world, "bravo", "full", ApiKeyScopes.AccountsRead, ApiKeyScopes.AccountsWrite, ApiKeyScopes.AccountsDelete),
        };

        return new BillingContractWorld(world, agent, backup, small, large, keys);
    }

    /// <summary>Signs a reseller in and creates a key of its own through the reseller's key endpoint.</summary>
    /// <param name="world">The seeded panel.</param>
    /// <param name="reseller">The reseller's login name, <c>alpha</c> or <c>bravo</c>.</param>
    /// <param name="name">The key's name.</param>
    /// <param name="scopes">The scopes to grant.</param>
    /// <returns>The key as its creator saw it.</returns>
    public static async Task<ApiKeyIssued> IssueKeyAsync(
        ResellerWorld world,
        string reseller,
        string name,
        params string[] scopes)
    {
        using var login = await world.SignInAsync(reseller);
        var response = await login.PostAsJsonAsync(KeysRoute, new { Name = name, Scopes = scopes });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var key = body.RootElement.GetProperty("key");

        return new ApiKeyIssued(
            key.GetProperty("id").GetGuid(),
            key.GetProperty("prefix").GetString()!,
            body.RootElement.GetProperty("secret").GetString()!);
    }

    /// <summary>Builds what a replay of the billing contract is given by this world.</summary>
    /// <returns>The client, the inputs, the three keys and the two host conditions this world can create.</returns>
    public BillingReplayEnvironment ReplayEnvironment()
    {
        var inputs = new Dictionary<string, string>
        {
            ["planId"] = World.Alpha.PlanId.ToString(),
            ["smallPlanId"] = SmallPlanId.ToString(),
            ["largePlanId"] = LargePlanId.ToString(),
            ["fullPoolPlanId"] = World.Alpha.PlanId.ToString(),
            ["foreignPlanId"] = World.Bravo.PlanId.ToString(),
            ["foreignAccountId"] = World.Bravo.AccountId.ToString(),
        };
        var credentials = new Dictionary<string, string>
        {
            ["full"] = Full.Secret,
            ["provisioner"] = Provisioner.Secret,
            ["reader"] = Reader.Secret,
        };

        return new BillingReplayEnvironment(_client, inputs, credentials, [DiskArrangement(), BackupArrangement()]);
    }

    /// <summary>Sends one call to the reseller API as an integration does.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The route relative to <see cref="ProvisioningRoute"/>.</param>
    /// <param name="secret">The key's plaintext, or <c>null</c> to send no key.</param>
    /// <param name="idempotencyKey">The <c>Idempotency-Key</c>, or <c>null</c> to send none.</param>
    /// <param name="body">The JSON body, or <c>null</c> for none.</param>
    /// <returns>The panel's answer.</returns>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? secret,
        string? idempotencyKey = null,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, ProvisioningRoute + path);
        if (secret is not null)
        {
            request.Headers.Add("X-Api-Key", secret);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add(IdempotencyHeaders.Key, idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return await _client.SendAsync(request);
    }

    /// <summary>Reads an account as it is stored, whoever owns it.</summary>
    /// <param name="accountId">The account's id.</param>
    /// <returns>The stored account, or <c>null</c> when there is none.</returns>
    public async Task<Account?> StoredAccountAsync(Guid accountId)
    {
        return await World.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(account => account.Id == accountId);
        });
    }

    /// <summary>Releases the client and the host.</summary>
    /// <returns>Resolves once both are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await World.DisposeAsync();
    }

    /// <summary>The condition under which the host measures more disk than the small plan allows.</summary>
    /// <returns>The arrangement.</returns>
    private BillingArrangement DiskArrangement()
    {
        var previous = 0UL;

        return new BillingArrangement(
            "diskUsedAbovePlan",
            () =>
            {
                previous = Agent.UsedBytes;
                Agent.UsedBytes = OverUsageBytes;
                return Task.CompletedTask;
            },
            () =>
            {
                Agent.UsedBytes = previous;
                return Task.CompletedTask;
            });
    }

    /// <summary>The condition under which the final backup of a termination fails.</summary>
    /// <returns>The arrangement.</returns>
    private BillingArrangement BackupArrangement()
    {
        return new BillingArrangement(
            "finalBackupFails",
            () =>
            {
                Backup.CreateFailureCode = BackupFailureCode;
                return Task.CompletedTask;
            },
            () =>
            {
                Backup.CreateFailureCode = null;
                return Task.CompletedTask;
            });
    }
}
