using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The account-name rules of <c>POST /api/v1/accounts</c> over real HTTP against a real PostgreSQL
/// (issue #83): a reserved name and a name outside the agent's alphabet are refused with a 400 and a
/// machine code (the localized wording is pinned in <c>ErrorMessageLocalizationTests</c>), before anything reaches the agent and before a
/// row is stored. The agent is the boundary that protects the host; these tests pin the API half that
/// tells the administrator why, early, instead of an agent error after the request was accepted.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AccountNameRulesEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountNameRulesEndpointTests(PostgresFixture postgres)
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
    /// A name the host already gives a meaning to is refused with <c>AccountNameReserved</c>, the
    /// agent is never asked, and no account row is stored. One name per class of the threat note.
    /// </summary>
    /// <param name="name">A reserved name.</param>
    [Theory]
    [InlineData("root")]
    [InlineData("redis")]
    [InlineData("sudo")]
    [InlineData("postmaster")]
    [InlineData("ubuntu")]
    [InlineData("maran")]
    [InlineData("maran_api")]
    public async Task Creating_an_account_with_a_reserved_name_is_refused_reaches_no_agent_and_stores_no_row(string name)
    {
        var agent = new RecordingCreateAgentAccountsClient();
        await using var factory = CreateFactory(agent);
        var (client, planId) = await SignedInWithAPlanAsync(factory);
        using var admin = client;

        var response = await CreateAsync(admin, name, planId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("AccountNameReserved", await CodeOfAsync(response));
        Assert.Empty(agent.Created);
        Assert.Equal(0, await StoredAccountsAsync(factory, name));
    }

    /// <summary>
    /// The inverse control of the refusals: an ordinary name, and one that merely begins with a
    /// reserved one, reaches the agent exactly once. Without it the refusals above could pass against a
    /// route that refuses every creation.
    /// </summary>
    /// <param name="name">A name that is not reserved.</param>
    [Theory]
    [InlineData("acme")]
    [InlineData("client_42")]
    [InlineData("rooted")]
    [InlineData("a23")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123")]
    public async Task Creating_an_account_with_an_ordinary_name_reaches_the_agent_once(string name)
    {
        var agent = new RecordingCreateAgentAccountsClient();
        await using var factory = CreateFactory(agent);
        var (client, planId) = await SignedInWithAPlanAsync(factory);
        using var admin = client;

        var response = await CreateAsync(admin, name, planId);

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([name], agent.Created);
    }

    /// <summary>
    /// A name the agent would refuse as invalid input is refused at the API with
    /// <c>AccountNameInvalid</c> and never handed to the agent: a hyphen, a name shorter than three or
    /// longer than thirty characters, an uppercase letter and a leading digit.
    /// </summary>
    /// <param name="name">A name outside the agent's alphabet.</param>
    [Theory]
    [InlineData("acme-01")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxyz01234")]
    [InlineData("Acme")]
    [InlineData("1acme")]
    public async Task Creating_an_account_with_a_name_outside_the_agents_alphabet_is_refused_and_never_handed_to_the_agent(
        string name)
    {
        var agent = new RecordingCreateAgentAccountsClient();
        await using var factory = CreateFactory(agent);
        var (client, planId) = await SignedInWithAPlanAsync(factory);
        using var admin = client;

        var response = await CreateAsync(admin, name, planId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("AccountNameInvalid", await CodeOfAsync(response));
        Assert.Empty(agent.Created);
        Assert.Equal(0, await StoredAccountsAsync(factory, name));
    }

    /// <summary>Posts a creation request.</summary>
    /// <param name="admin">A signed-in administrator's client.</param>
    /// <param name="name">The account name to ask for.</param>
    /// <param name="planId">An existing plan.</param>
    /// <returns>The response.</returns>
    private static Task<HttpResponseMessage> CreateAsync(HttpClient admin, string name, Guid planId)
    {
        return admin.PostAsJsonAsync("/api/v1/accounts", Body(name, planId));
    }

    /// <summary>A creation request body whose only variable is the name.</summary>
    /// <param name="name">The account name to ask for.</param>
    /// <param name="planId">An existing plan.</param>
    /// <returns>The body.</returns>
    private static object Body(string name, Guid planId)
    {
        return new
        {
            Name = name,
            PrimaryDomain = $"{name.Replace('_', '-')}.example.com",
            PlanId = planId,
            OwnerEmail = "owner@example.com",
        };
    }

    /// <summary>Reads the machine <c>code</c> out of a problem document.</summary>
    /// <param name="response">The refused response.</param>
    /// <returns>The code.</returns>
    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Counts the stored accounts of one name.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="name">The account name.</param>
    /// <returns>The row count.</returns>
    private static async Task<int> StoredAccountsAsync(WebApplicationFactory<Program> factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

        return await context.Accounts.CountAsync(account => account.Name == name);
    }

    /// <summary>Boots the host with the recording agent in place of the real one.</summary>
    /// <param name="agent">The recording agent.</param>
    /// <returns>The factory, which the caller disposes.</returns>
    private WebApplicationFactory<Program> CreateFactory(RecordingCreateAgentAccountsClient agent)
    {
        return EndpointHarness.CreateFactory(_pg, services =>
        {
            services.RemoveAll<IAgentAccountsClient>();
            services.AddSingleton<IAgentAccountsClient>(agent);
        });
    }

    /// <summary>Migrates, seeds an administrator and a plan, and signs the administrator in.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The administrator's client and the plan's id.</returns>
    private static async Task<(HttpClient Admin, Guid PlanId)> SignedInWithAPlanAsync(
        WebApplicationFactory<Program> factory)
    {
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (planId, _) = await EndpointHarness.SeedAccountAsync(factory, "seeded");

        return (await EndpointHarness.SignInAsync(factory, "admin"), planId);
    }
}
