using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The SFTP public-key endpoints (<c>/api/v1/sftp-users/{id}/keys</c>, issues #135 and #339) over real
/// HTTP against real PostgreSQL, with real <c>ssh-keygen</c> keys and an agent double that behaves like
/// the host: it keeps the last complete key set it was sent and records every request to end sessions.
/// Who may reach the surface (tenant isolation, anonymous callers, view-as) is in
/// <see cref="SftpKeyAccessTests"/>. Threat note:
/// docs/superpowers/notes/2026-11-05-sftp-public-keys-threat-note.md.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class SftpKeyEndpointTests : IAsyncLifetime
{
    /// <summary>The agent's failure as the real client reports it when the host's call fails.</summary>
    private static readonly Error AgentFailure = Error.Of("AgentSystemFailure", ErrorType.Failure);

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SftpKeyEndpointTests(PostgresFixture postgres)
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
    /// Adding an Ed25519, an ECDSA and an RSA key answers 201 with the fingerprint <c>ssh-keygen</c>
    /// printed, never returns the key itself, and leaves the agent holding the full set in order.
    /// </summary>
    /// <remarks>
    /// The fingerprints are pasted from <c>ssh-keygen -l</c>, so the panel's computation is held against
    /// the tool that defines the format. The stored lines are compared with the pasted ones minus their
    /// comment, which is what the agent writes into the file sshd reads.
    /// </remarks>
    [Fact]
    public async Task Adding_real_keys_answers_with_their_fingerprints_and_sends_the_agent_the_full_set()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var ed25519 = await AddAsync(owner, login, "laptop", SftpKeyFixtures.Ed25519);
        var ecdsa = await AddAsync(owner, login, "ci", SftpKeyFixtures.Ecdsa);
        var rsa = await AddAsync(owner, login, "legacy", SftpKeyFixtures.Rsa);

        Assert.Equal(HttpStatusCode.Created, ed25519.StatusCode);
        Assert.Equal(HttpStatusCode.Created, ecdsa.StatusCode);
        Assert.Equal(HttpStatusCode.Created, rsa.StatusCode);
        using var created = JsonDocument.Parse(await ed25519.Content.ReadAsStringAsync());
        Assert.Equal("laptop", created.RootElement.GetProperty("label").GetString());
        Assert.Equal("ssh-ed25519", created.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal(SftpKeyFixtures.Ed25519Fingerprint, created.RootElement.GetProperty("fingerprint").GetString());
        Assert.False(created.RootElement.TryGetProperty("publicKey", out _));
        Assert.Equal(SftpKeyFixtures.EcdsaFingerprint, await FingerprintOfAsync(ecdsa));
        Assert.Equal(SftpKeyFixtures.RsaFingerprint, await FingerprintOfAsync(rsa));

        // One request per add, each carrying everything the login should hold afterwards.
        var calls = agent.SetCalls;
        Assert.Equal(3, calls.Count);
        Assert.All(calls, call => { Assert.Equal(("acme", "deploy"), (call.AccountUsername, call.SftpUsername)); });
        Assert.Equal([SftpKeyFixtures.Ed25519Stored], calls[0].Keys);
        Assert.Equal(3, calls[2].Keys.Count);
        Assert.Equal(calls[2].Keys, agent.KeysOf("acme", "deploy"));
        Assert.All(calls[2].Keys, line => { Assert.DoesNotContain("@laptop", line, StringComparison.Ordinal); });

        // Adding a key must never cost the customer an open session.
        Assert.Empty(agent.EndCalls);
    }

    /// <summary>
    /// The list shows label, algorithm and fingerprint for each key and never the key text, pages and
    /// searches on the server, and sorts on the label.
    /// </summary>
    [Fact]
    public async Task Listing_keys_shows_fingerprints_never_the_key_and_pages_searches_and_sorts_on_the_server()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        await SftpKeyWorld.SeedKeyAsync(factory, login, "zulu laptop", SftpKeyFixtures.Ed25519);
        await SftpKeyWorld.SeedKeyAsync(factory, login, "alpha ci", SftpKeyFixtures.Ecdsa);
        await SftpKeyWorld.SeedKeyAsync(factory, login, "mid legacy", SftpKeyFixtures.Rsa);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        using var all = await GetJsonAsync(owner, KeysPath(login));
        using var byLabel = await GetJsonAsync(owner, KeysPath(login) + "?sort=label&pageSize=10");
        using var descending = await GetJsonAsync(owner, KeysPath(login) + "?page=1&pageSize=10&sort=-label");
        using var searched = await GetJsonAsync(owner, KeysPath(login) + "?search=LEGACY");

        Assert.Equal(3, all.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(
            [SftpKeyFixtures.Ed25519Fingerprint, SftpKeyFixtures.EcdsaFingerprint, SftpKeyFixtures.RsaFingerprint],
            Items(all).Select(item => { return item.GetProperty("fingerprint").GetString()!; }));
        Assert.All(Items(all), item =>
        {
            Assert.False(item.TryGetProperty("publicKey", out _));
            Assert.DoesNotContain("AAAA", item.GetRawText(), StringComparison.Ordinal);
        });
        Assert.Equal(["alpha ci", "mid legacy", "zulu laptop"], Labels(byLabel));
        Assert.Equal(["zulu laptop", "mid legacy", "alpha ci"], Labels(descending));
        Assert.Equal(["mid legacy"], Labels(searched));
        Assert.Equal(1, searched.RootElement.GetProperty("total").GetInt32());
        Assert.Empty(agent.SetCalls);
    }

    /// <summary>
    /// Removing a key answers 204, sends the agent only the keys that remain, ends the login's
    /// sessions, and removing it again is a 404 that touches nothing.
    /// </summary>
    [Fact]
    public async Task Removing_a_key_sends_the_remaining_set_ends_the_sessions_and_a_repeat_is_not_found()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        var removed = await SftpKeyWorld.SeedKeyAsync(factory, login, "laptop", SftpKeyFixtures.Ed25519);
        await SftpKeyWorld.SeedKeyAsync(factory, login, "ci", SftpKeyFixtures.Ecdsa);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var response = await owner.DeleteAsync(KeyPath(login, removed));
        var repeated = await owner.DeleteAsync(KeyPath(login, removed));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var set = Assert.Single(agent.SetCalls);
        Assert.Equal([SftpKeyFixtures.EcdsaStored], set.Keys);
        Assert.Equal(["acme/deploy"], agent.EndCalls);

        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        Assert.Equal("SftpKeyNotFound", await ProblemDocument.CodeAsync(repeated));
        Assert.Single(agent.SetCalls);
        Assert.Single(agent.EndCalls);
        var remaining = Assert.Single(await SftpKeyWorld.KeyRowsAsync(factory));
        Assert.Equal(SftpKeyFixtures.EcdsaFingerprint, remaining.Fingerprint);
    }

    /// <summary>Removing the last key sends an empty set, which is how the agent clears the file.</summary>
    [Fact]
    public async Task Removing_the_last_key_sends_an_empty_set()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        var only = await SftpKeyWorld.SeedKeyAsync(factory, login, "laptop", SftpKeyFixtures.Ed25519);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var response = await owner.DeleteAsync(KeyPath(login, only));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(Assert.Single(agent.SetCalls).Keys);
        Assert.Empty(await SftpKeyWorld.KeyRowsAsync(factory));
    }

    /// <summary>
    /// A line the grammar refuses answers 400 with <c>SftpKeyInvalid</c>, and nothing is stored, sent to
    /// the agent, or echoed back.
    /// </summary>
    /// <param name="publicKey">The refused text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not a key")]
    [InlineData("ssh-ed25519")]
    [InlineData("command=\"touch /tmp/x\" " + SftpKeyFixtures.Ed25519Stored)]
    [InlineData("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIIUJRnDAZEkPKAlf7FOOY4fWWstilA67ufNEywcO3LFG\nssh-ed25519 AAAA")]
    [InlineData("ssh-rsa AAAAC3NzaC1lZDI1NTE5AAAAIIUJRnDAZEkPKAlf7FOOY4fWWstilA67ufNEywcO3LFG")]
    [InlineData(SftpKeyFixtures.Rsa1024)]
    public async Task A_key_the_grammar_refuses_answers_400_and_nothing_is_stored_or_sent(string publicKey)
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var response = await AddAsync(owner, login, "laptop", publicKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SftpKeyInvalid", await ProblemDocument.CodeAsync(response));
        Assert.DoesNotContain("AAAA", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Empty(agent.SetCalls);
        Assert.Empty(await SftpKeyWorld.KeyRowsAsync(factory));
    }

    /// <summary>A label that is empty, too long or carries a control character answers 400 with its own code.</summary>
    /// <param name="label">The refused label.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    [InlineData("two\nlines")]
    public async Task A_label_the_rules_refuse_answers_400_and_nothing_is_stored_or_sent(string label)
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var response = await AddAsync(owner, login, label, SftpKeyFixtures.Ed25519);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SftpKeyLabelInvalid", await ProblemDocument.CodeAsync(response));
        Assert.Empty(agent.SetCalls);
        Assert.Empty(await SftpKeyWorld.KeyRowsAsync(factory));
    }

    /// <summary>
    /// The same key twice on one login is a 409 that writes and sends nothing — even with another label
    /// and another comment — while the same key on a different login is accepted.
    /// </summary>
    /// <remarks>
    /// The second half is the inverse control: a duplicate check keyed on the fingerprint alone, across
    /// logins, would refuse it too.
    /// </remarks>
    [Fact]
    public async Task The_same_key_twice_on_one_login_is_a_conflict_and_on_another_login_is_accepted()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        var second = await SftpKeyWorld.SeedLoginAsync(factory, "acme", "backup");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        await AddAsync(owner, login, "laptop", SftpKeyFixtures.Ed25519);

        var again = await AddAsync(
            owner, login, "renamed", SftpKeyFixtures.Ed25519Stored + " elsewhere");
        var otherLogin = await AddAsync(owner, second, "laptop", SftpKeyFixtures.Ed25519);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("SftpKeyDuplicate", await ProblemDocument.CodeAsync(again));
        Assert.Equal(HttpStatusCode.Created, otherLogin.StatusCode);
        Assert.Equal(2, agent.SetCalls.Count);
        Assert.Equal(2, (await SftpKeyWorld.KeyRowsAsync(factory)).Count);
    }

    /// <summary>
    /// The 33rd key on a login is a 409 <c>SftpKeyLimitReached</c> that sends nothing to the agent; the
    /// 32nd is accepted and the agent receives all thirty-two.
    /// </summary>
    /// <remarks>
    /// The filler keys are synthetic, well-formed Ed25519 lines with no private half; the key under
    /// test is real. 32 is the agent's own ceiling, so a set the panel let through would be refused
    /// whole by the agent.
    /// </remarks>
    [Fact]
    public async Task The_thirty_third_key_is_a_conflict_and_the_thirty_second_is_accepted()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        await SftpKeyWorld.SeedSyntheticKeysAsync(factory, login, 31);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var last = await AddAsync(owner, login, "last", SftpKeyFixtures.Ed25519);
        var over = await AddAsync(owner, login, "over", SftpKeyFixtures.Ecdsa);

        Assert.Equal(HttpStatusCode.Created, last.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("SftpKeyLimitReached", await ProblemDocument.CodeAsync(over));
        var sent = Assert.Single(agent.SetCalls);
        Assert.Equal(32, sent.Keys.Count);
        Assert.Equal(32, (await SftpKeyWorld.KeyRowsAsync(factory)).Count);
    }

    /// <summary>
    /// When the agent refuses the set on an add, the answer carries the agent's code, no row is left,
    /// the host's set is as it was, and the refusal is journalled; once the agent recovers the same
    /// request succeeds.
    /// </summary>
    [Fact]
    public async Task An_agent_that_refuses_the_set_on_an_add_leaves_the_database_and_the_host_unchanged()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        await SftpKeyWorld.SeedKeyAsync(factory, login, "laptop", SftpKeyFixtures.Ed25519);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        agent.RefuseSets(AgentFailure);

        var refused = await AddAsync(owner, login, "ci", SftpKeyFixtures.Ecdsa);

        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);
        Assert.Equal("AgentSystemFailure", await ProblemDocument.CodeAsync(refused));
        var row = Assert.Single(await SftpKeyWorld.KeyRowsAsync(factory));
        Assert.Equal(SftpKeyFixtures.Ed25519Fingerprint, row.Fingerprint);
        Assert.Empty(agent.KeysOf("acme", "deploy"));
        var entry = Assert.Single(await SubUserTeamWorld.AuditEntriesAsync(factory, SftpAuditActions.SftpKeyAdded));
        Assert.False(entry.Succeeded);
        Assert.Contains(SftpKeyFixtures.EcdsaFingerprint, entry.Subject, StringComparison.Ordinal);

        agent.RefuseSets(null);
        var retried = await AddAsync(owner, login, "ci", SftpKeyFixtures.Ecdsa);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal(2, (await SftpKeyWorld.KeyRowsAsync(factory)).Count);
    }

    /// <summary>
    /// When the agent refuses the set on a removal, the key stays listed, no session is ended, and the
    /// refusal is journalled with the fingerprint.
    /// </summary>
    [Fact]
    public async Task An_agent_that_refuses_the_set_on_a_removal_keeps_the_key_listed_and_ends_no_session()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        var key = await SftpKeyWorld.SeedKeyAsync(factory, login, "laptop", SftpKeyFixtures.Ed25519);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        agent.RefuseSets(AgentFailure);

        var refused = await owner.DeleteAsync(KeyPath(login, key));

        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);
        Assert.Equal("AgentSystemFailure", await ProblemDocument.CodeAsync(refused));
        Assert.Single(await SftpKeyWorld.KeyRowsAsync(factory));
        Assert.Empty(agent.EndCalls);
        var entry = Assert.Single(await SubUserTeamWorld.AuditEntriesAsync(factory, SftpAuditActions.SftpKeyRemoved));
        Assert.False(entry.Succeeded);
        Assert.Contains(SftpKeyFixtures.Ed25519Fingerprint, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// When ending the sessions fails after the set was replaced, the caller is told with
    /// <c>SftpSessionsNotEnded</c> and the row is rolled back so a repeat can end them; the repeat
    /// succeeds once the agent does.
    /// </summary>
    [Fact]
    public async Task A_failure_to_end_the_sessions_is_reported_rolls_the_row_back_and_a_repeat_converges()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        var key = await SftpKeyWorld.SeedKeyAsync(factory, login, "laptop", SftpKeyFixtures.Ed25519);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        agent.RefuseEndSessions(AgentFailure);

        var refused = await owner.DeleteAsync(KeyPath(login, key));

        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);
        Assert.Equal("SftpSessionsNotEnded", await ProblemDocument.CodeAsync(refused));
        Assert.Single(await SftpKeyWorld.KeyRowsAsync(factory));

        // The safe direction: the host already refuses the key even though the panel still lists it.
        Assert.Empty(agent.KeysOf("acme", "deploy"));
        var failed = Assert.Single(await SubUserTeamWorld.AuditEntriesAsync(factory, SftpAuditActions.SftpKeyRemoved));
        Assert.False(failed.Succeeded);

        agent.RefuseEndSessions(null);
        var repeated = await owner.DeleteAsync(KeyPath(login, key));

        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Empty(await SftpKeyWorld.KeyRowsAsync(factory));
        Assert.Equal(2, agent.EndCalls.Count);
    }

    /// <summary>A host that holds no such login has no session to end, so the removal still completes.</summary>
    [Fact]
    public async Task A_host_that_does_not_hold_the_login_still_lets_the_removal_complete()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        var key = await SftpKeyWorld.SeedKeyAsync(factory, login, "laptop", SftpKeyFixtures.Ed25519);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        agent.RefuseEndSessions(Error.Of("AgentNotFound", ErrorType.NotFound));

        var response = await owner.DeleteAsync(KeyPath(login, key));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await SftpKeyWorld.KeyRowsAsync(factory));
    }

    /// <summary>
    /// A successful add and a successful removal each write one journal entry naming the actor, the login,
    /// the label and the fingerprint — and nothing of the key body or the comment of the pasted line.
    /// </summary>
    [Fact]
    public async Task Adding_and_removing_a_key_are_journalled_with_the_fingerprint_and_never_the_key()
    {
        var agent = new RecordingKeySftpAgent();
        await using var factory = await SftpKeyWorld.BootAsync(_pg, agent);
        var login = await SftpKeyWorld.LoginIdAsync(factory, "acme_deploy");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var added = await AddAsync(owner, login, "laptop", SftpKeyFixtures.Ed25519);
        var keyId = await IdOfAsync(added);
        var removed = await owner.DeleteAsync(KeyPath(login, keyId));

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var entries = await SubUserTeamWorld.AuditEntriesAsync(
            factory, SftpAuditActions.SftpKeyAdded, SftpAuditActions.SftpKeyRemoved);
        Assert.Equal(
            [SftpAuditActions.SftpKeyAdded, SftpAuditActions.SftpKeyRemoved],
            entries.Select(entry => { return entry.Action; }));
        Assert.All(entries, entry =>
        {
            Assert.True(entry.Succeeded);
            Assert.Equal("owner-a", entry.ActorUsername);
            Assert.Contains("deploy", entry.Subject, StringComparison.Ordinal);
            Assert.Contains("laptop", entry.Subject, StringComparison.Ordinal);
            Assert.Contains(SftpKeyFixtures.Ed25519Fingerprint, entry.Subject, StringComparison.Ordinal);

            // The key blob and the pasted comment: a fingerprint is an identifier, these are not.
            Assert.DoesNotContain("AAAAC3NzaC1lZDI1NTE5", entry.Subject, StringComparison.Ordinal);
            Assert.DoesNotContain("first@laptop", entry.Subject, StringComparison.Ordinal);
        });
    }

    /// <summary>Posts one key for a login.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="loginId">The login the key is for.</param>
    /// <param name="label">The key's label.</param>
    /// <param name="publicKey">The pasted key line.</param>
    private static Task<HttpResponseMessage> AddAsync(HttpClient client, Guid loginId, string label, string publicKey)
    {
        return client.PostAsJsonAsync(KeysPath(loginId), new { label, publicKey });
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

    /// <summary>Reads a successful JSON answer.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="path">The path and query string.</param>
    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The items of a page.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<JsonElement> Items(JsonDocument page)
    {
        return [.. page.RootElement.GetProperty("items").EnumerateArray()];
    }

    /// <summary>The labels on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<string> Labels(JsonDocument page)
    {
        return [.. Items(page).Select(item => { return item.GetProperty("label").GetString()!; })];
    }

    /// <summary>Reads the fingerprint out of a created key's answer.</summary>
    /// <param name="response">The 201 answer.</param>
    private static async Task<string> FingerprintOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("fingerprint").GetString()!;
    }

    /// <summary>Reads the identifier out of a created key's answer.</summary>
    /// <param name="response">The 201 answer.</param>
    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }
}
