using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Who may reach the SFTP key endpoints (<c>/api/v1/sftp-users/{id}/keys</c>): the tenant IDOR cases
/// the Definition of Done requires for all three verbs, anonymous callers, and an operator viewing the
/// panel as a customer. What the endpoints do for a permitted caller is in
/// <see cref="SftpKeyEndpointTests"/>.
/// </summary>
/// <remarks>
/// Every refusal below asserts the agent was not asked and the key rows are untouched: a 404 that still
/// wrote a key onto another customer's login would be the worst failure this surface can have, and the
/// status alone would not show it.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SftpKeyAccessTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SftpKeyAccessTests(PostgresFixture postgres)
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
    /// The IDOR case for all three verbs: customer A listing, adding to and removing a key from customer
    /// B's login gets 404 — never 403 — with the same answer an invented login gets, and nothing changes.
    /// </summary>
    /// <remarks>
    /// The owner of the login reading the same key first is the control: without it a 404 could mean the
    /// seed never ran. A is also refused a key id of ITS OWN other login under the wrong login id.
    /// </remarks>
    [Fact]
    public async Task A_customer_reaching_for_another_customers_login_keys_is_answered_not_found_on_every_verb()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var strangerLogin = await SftpKeyWorld.LoginIdAsync(factory, "beta_deploy");
        var strangerKey = await SftpKeyWorld.SeedKeyAsync(factory, strangerLogin, "theirs", SftpKeyFixtures.Ed25519);
        using var stranger = await EndpointHarness.SignInAsync(factory, "owner-b");
        using var customer = await EndpointHarness.SignInAsync(factory, "owner-a");

        using (var control = JsonDocument.Parse(await stranger.GetStringAsync(KeysPath(strangerLogin))))
        {
            Assert.Equal(1, control.RootElement.GetProperty("total").GetInt32());
        }

        var list = await customer.GetAsync(KeysPath(strangerLogin));
        var add = await customer.PostAsJsonAsync(
            KeysPath(strangerLogin), new { label = "mine", publicKey = SftpKeyFixtures.Ecdsa });
        var remove = await customer.DeleteAsync(KeyPath(strangerLogin, strangerKey));
        var invented = await customer.GetAsync(KeysPath(Guid.NewGuid()));

        foreach (var response in new[] { list, add, remove })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("SftpUserNotFound", await ProblemDocument.CodeAsync(response));
        }

        // The same signature as a login that does not exist, so the status is no oracle for ids.
        Assert.Equal(await ProblemDocument.SignatureAsync(invented), await ProblemDocument.SignatureAsync(list));

        Assert.Empty(agent.SetCalls);
        Assert.Empty(agent.EndCalls);
        var row = Assert.Single(await SftpKeyWorld.KeyRowsAsync(factory));
        Assert.Equal(strangerKey, row.Id);
    }

    /// <summary>
    /// A key id of another login — of the same account, or of another account — is not found under a
    /// login the caller does own, so the route cannot delete a key by guessing its id.
    /// </summary>
    [Fact]
    public async Task A_key_of_another_login_is_not_found_under_a_login_the_caller_owns()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var own = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        var sibling = await SftpKeyWorld.SeedLoginAsync(factory, "acme", "backup");
        var siblingKey = await SftpKeyWorld.SeedKeyAsync(factory, sibling, "sibling", SftpKeyFixtures.Ed25519);
        var strangerLogin = await SftpKeyWorld.LoginIdAsync(factory, "beta_deploy");
        var strangerKey = await SftpKeyWorld.SeedKeyAsync(factory, strangerLogin, "theirs", SftpKeyFixtures.Ecdsa);
        using var customer = await EndpointHarness.SignInAsync(factory, "owner-a");

        var ofSibling = await customer.DeleteAsync(KeyPath(own, siblingKey));
        var ofStranger = await customer.DeleteAsync(KeyPath(own, strangerKey));

        foreach (var response in new[] { ofSibling, ofStranger })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("SftpKeyNotFound", await ProblemDocument.CodeAsync(response));
        }

        Assert.Empty(agent.SetCalls);
        Assert.Empty(agent.EndCalls);
        Assert.Equal(2, (await SftpKeyWorld.KeyRowsAsync(factory)).Count);
    }

    /// <summary>
    /// A probe of another customer's login leaves a refusal in the journal naming the identifier probed
    /// for, and no entry claims a key was added or removed.
    /// </summary>
    [Fact]
    public async Task A_probe_of_another_customers_login_is_journalled_as_a_refusal_under_the_supplied_id()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var strangerLogin = await SftpKeyWorld.LoginIdAsync(factory, "beta_deploy");
        var strangerKey = await SftpKeyWorld.SeedKeyAsync(factory, strangerLogin, "theirs", SftpKeyFixtures.Ed25519);
        using var customer = await EndpointHarness.SignInAsync(factory, "owner-a");

        await customer.PostAsJsonAsync(
            KeysPath(strangerLogin), new { label = "mine", publicKey = SftpKeyFixtures.Ecdsa });
        await customer.DeleteAsync(KeyPath(strangerLogin, strangerKey));

        var entries = await SubUserTeamWorld.AuditEntriesAsync(
            factory, SftpAuditActions.SftpKeyAdded, SftpAuditActions.SftpKeyRemoved);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.False(entry.Succeeded);
            Assert.Equal("owner-a", entry.ActorUsername);
            Assert.Equal(strangerLogin.ToString(), entry.Subject);
        });
    }

    /// <summary>An anonymous caller is refused by all three key routes with 401.</summary>
    /// <param name="method">The HTTP method.</param>
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task An_anonymous_caller_is_refused_by_every_key_route(string method)
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        var key = await SftpKeyWorld.SeedKeyAsync(factory, login, "laptop", SftpKeyFixtures.Ed25519);
        using var anonymous = factory.CreateClient();
        using var request = new HttpRequestMessage(
            new HttpMethod(method), method == "DELETE" ? KeyPath(login, key) : KeysPath(login));
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { label = "x", publicKey = SftpKeyFixtures.Ecdsa });
        }

        var response = await anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(agent.SetCalls);
        Assert.Single(await SftpKeyWorld.KeyRowsAsync(factory));
    }

    /// <summary>
    /// An operator viewing as the customer may read the keys but is refused adding and removing with the
    /// view-as code, because a key outlives the look; the agent is not asked and nothing changes.
    /// </summary>
    [Fact]
    public async Task A_view_as_session_may_list_keys_but_is_refused_adding_and_removing_them()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        var key = await SftpKeyWorld.SeedKeyAsync(factory, login, "laptop", SftpKeyFixtures.Ed25519);
        using var viewing = await SubUserTeamWorld.StartViewingAsync(factory, "acme");

        var list = await viewing.GetAsync(KeysPath(login));
        var add = await viewing.PostAsJsonAsync(
            KeysPath(login), new { label = "x", publicKey = SftpKeyFixtures.Ecdsa });
        var remove = await viewing.DeleteAsync(KeyPath(login, key));

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using (var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
        {
            var first = page.RootElement.GetProperty("items")[0];
            Assert.Equal(SftpKeyFixtures.Ed25519Fingerprint, first.GetProperty("fingerprint").GetString());
        }

        foreach (var refused in new[] { add, remove })
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("HostViewAsNotAllowed", await ProblemDocument.CodeAsync(refused));
        }

        Assert.Empty(agent.SetCalls);
        Assert.Empty(agent.EndCalls);
        Assert.Single(await SftpKeyWorld.KeyRowsAsync(factory));
    }

    /// <summary>The path of one login's keys.</summary>
    /// <param name="loginId">The login.</param>
    private static string KeysPath(Guid loginId)
    {
        return $"/api/v1/sftp-users/{loginId}/keys";
    }

    /// <summary>The path of one key of one login.</summary>
    /// <param name="loginId">The login.</param>
    /// <param name="keyId">The key.</param>
    private static string KeyPath(Guid loginId, Guid keyId)
    {
        return $"/api/v1/sftp-users/{loginId}/keys/{keyId}";
    }
}
