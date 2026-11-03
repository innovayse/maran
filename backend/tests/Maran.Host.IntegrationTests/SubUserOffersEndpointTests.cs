using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// An account's owner offers membership to a login that already exists, over real HTTP (issue #328):
/// the answer is the same whatever the typed identifier names, an offer is stored and a plain mail sent
/// only for a login that qualifies, and every caller who is not the owner is refused. Threat note:
/// docs/superpowers/notes/2026-11-03-membership-offers-threat-note.md.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: the mail transport itself (the recording mailer stands in for the SMTP server), and
/// the timing difference between the honoured and the withheld path, which the threat note states as a
/// residual and which no assertion in this file claims to close.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SubUserOffersEndpointTests : IAsyncLifetime
{
    /// <summary>The route that offers membership to an existing login.</summary>
    private const string Endpoint = "/api/v1/me/sub-users/existing";

    /// <summary>The grant list naming only the databases feature.</summary>
    private static readonly string[] DatabasesFeatures = { "databases" };

    /// <summary>The grant list naming a feature the panel does not have.</summary>
    private static readonly string[] UnknownFeatures = { "root" };

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The mail server's double: records what the panel mails.</summary>
    private readonly RecordingMailer _mailer = new(TimeSpan.Zero);

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SubUserOffersEndpointTests(PostgresFixture postgres)
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
    /// Offering to a qualifying login stores one open offer, mails that login a plain notice that names
    /// the account and carries no token or owner address, and shows the owner a pending row.
    /// </summary>
    [Fact]
    public async Task Offering_to_an_existing_login_stores_an_offer_mails_a_plain_notice_and_lists_a_pending_row()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var response = await OfferAsync(owner, "pat");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
        var mail = await SubUserTeamWorld.MailToAsync(_mailer, "pat@example.com");
        Assert.Contains("acme", mail.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token=", mail.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner-a@example.com", mail.Body, StringComparison.OrdinalIgnoreCase);
        using var list = JsonDocument.Parse(await owner.GetStringAsync("/api/v1/me/sub-users"));
        var row = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("invited", row.GetProperty("state").GetString());
        Assert.True(row.GetProperty("isOffer").GetBoolean());
        Assert.NotEqual(await SubUserTeamWorld.UserIdAsync(factory, "pat"), row.GetProperty("id").GetGuid());
        Assert.Equal("databases", Assert.Single(row.GetProperty("features").EnumerateArray()).GetString());
        Assert.Equal(0, await SubUserTeamWorld.MembershipCountAsync(factory, "pat", "acme"));
    }

    /// <summary>The identifier may be the login name or the e-mail address: either finds the login.</summary>
    [Fact]
    public async Task The_identifier_may_be_the_login_name_or_the_address()
    {
        await using var factory = await BootAsync();
        await SubUserTeamWorld.SeedAccountlessCustomerAsync(factory, "newcomer");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var byName = await OfferAsync(owner, "pat");
        var byAddress = await OfferAsync(owner, "newcomer@example.com");

        Assert.Equal(HttpStatusCode.Accepted, byName.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, byAddress.StatusCode);
        Assert.Equal(1, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
        Assert.Equal(1, await SubUserTeamWorld.OpenOfferCountAsync(factory, "newcomer", "acme"));
    }

    /// <summary>
    /// A login that exists and qualifies, one that does not exist, an administrator, a login that has not
    /// accepted its invitation, a login already on the team and a stranger's address all get exactly the
    /// same answer, and only the first stored an offer and sent mail.
    /// </summary>
    [Fact]
    public async Task Every_identifier_is_answered_alike_and_only_a_qualifying_login_is_offered_anything()
    {
        await using var factory = await BootAsync();
        await SubUserTeamWorld.SeedInvitedLoginAsync(factory, "pending-person");
        await SubUserTeamWorld.SeedMemberAsync(factory, "deploy", AccountFeature.None);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var withheld = new[] { "ghost", "admin", "pending-person", "deploy", "stranger@elsewhere.example" };
        var answers = new List<HttpResponseMessage>();
        foreach (var identifier in withheld)
        {
            answers.Add(await OfferAsync(owner, identifier));
        }

        var honoured = await OfferAsync(owner, "pat");

        Assert.Equal(HttpStatusCode.Accepted, honoured.StatusCode);
        Assert.All(answers, answer => { Assert.Equal(HttpStatusCode.Accepted, answer.StatusCode); });
        Assert.All(answers, answer => { Assert.Equal(PropertyNames(honoured), PropertyNames(answer)); });
        await SubUserTeamWorld.MailToAsync(_mailer, "pat@example.com");
        Assert.Equal(1, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
        Assert.Equal(0, await SubUserTeamWorld.OpenOfferCountAsync(factory, "admin", "acme"));
        Assert.Equal(0, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pending-person", "acme"));
        Assert.Equal(0, await SubUserTeamWorld.OpenOfferCountAsync(factory, "deploy", "acme"));
        var recipients = _mailer.Sent.Select(mail => { return mail.Recipient; }).ToArray();
        Assert.Equal(["pat@example.com"], recipients);
    }

    /// <summary>A second offer to the same login for the same account stores nothing new and mails nobody again.</summary>
    [Fact]
    public async Task A_second_offer_to_the_same_login_is_answered_alike_and_adds_nothing()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var first = await OfferAsync(owner, "pat");
        await SubUserTeamWorld.MailToAsync(_mailer, "pat@example.com");

        var second = await OfferAsync(owner, "pat");

        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(PropertyNames(first), PropertyNames(second));
        Assert.Equal(1, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
        Assert.Single(_mailer.Sent);
    }

    /// <summary>A suspended account takes no offer: the request is answered like any other and stores nothing.</summary>
    [Fact]
    public async Task A_suspended_account_stores_no_offer_and_answers_alike()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        await SubUserTeamWorld.SuspendAccountAsync(factory, "acme");

        var response = await OfferAsync(owner, "pat");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(0, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
        Assert.Empty(_mailer.Sent);
    }

    /// <summary>
    /// Open offers count towards the team cap, and the cap is decided before the identifier is looked at:
    /// a full team is refused the same way whether the typed login exists or not.
    /// </summary>
    [Fact]
    public async Task Open_offers_count_towards_the_cap_whatever_login_is_typed()
    {
        await using var factory = await BootAsync(builder => { builder.UseSetting("SubUsers:MaxPerAccount", "1"); });
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var first = await OfferAsync(owner, "pat");

        var knownLogin = await OfferAsync(owner, "owner-b");
        var unknownLogin = await OfferAsync(owner, "ghost");
        var invitation = await owner.PostAsJsonAsync(
            "/api/v1/me/sub-users",
            new { Username = "fresh", Email = "fresh@example.com", Features = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, knownLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, unknownLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, invitation.StatusCode);
        foreach (var refused in new[] { knownLogin, unknownLogin })
        {
            using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
            Assert.Equal("SubUserLimitReached", body.RootElement.GetProperty("code").GetString());
        }
    }

    /// <summary>A feature that does not exist and an empty identifier are bad requests, and nothing is stored.</summary>
    [Fact]
    public async Task An_unknown_feature_and_an_empty_identifier_are_bad_requests()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var unknownFeature = await owner.PostAsJsonAsync(Endpoint, new { Identifier = "pat", Features = UnknownFeatures });
        var emptyIdentifier = await owner.PostAsJsonAsync(Endpoint, new { Identifier = "  ", Features = DatabasesFeatures });

        Assert.Equal(HttpStatusCode.BadRequest, unknownFeature.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyIdentifier.StatusCode);
        Assert.Equal(0, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
    }

    /// <summary>A team member, an administrator and an anonymous caller cannot offer membership.</summary>
    [Fact]
    public async Task Only_the_owner_can_offer_membership()
    {
        await using var factory = await BootAsync();
        await SubUserTeamWorld.SeedMemberAsync(factory, "deploy", AccountFeature.All);
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var anonymous = factory.CreateClient();

        var asMember = await OfferAsync(member, "pat");
        var asAdmin = await OfferAsync(admin, "pat");
        var asAnonymous = await OfferAsync(anonymous, "pat");

        Assert.Equal(HttpStatusCode.Forbidden, asMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
        using var body = JsonDocument.Parse(await asMember.Content.ReadAsStringAsync());
        Assert.Equal("SubUsersOwnerOnly", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
    }

    /// <summary>An operator viewing as the owner is refused with the view-as code, and no offer is stored.</summary>
    [Fact]
    public async Task A_view_as_token_cannot_offer_membership()
    {
        await using var factory = await BootAsync();
        using var viewing = await SubUserTeamWorld.StartViewingAsync(factory, "acme");

        var response = await OfferAsync(viewing, "pat");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("HostViewAsNotAllowed", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
    }

    /// <summary>An offer spends the per-account sending bucket an invitation spends, and is limited by it.</summary>
    [Fact]
    public async Task Offering_shares_the_invitation_sending_rate_limit()
    {
        await using var factory = await BootAsync(builder =>
        {
            builder.UseSetting("RateLimiting:InvitationSendMaxRequests", "1");
        });
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var first = await OfferAsync(owner, "pat");
        var second = await OfferAsync(owner, "owner-b");
        var invitation = await owner.PostAsJsonAsync(
            "/api/v1/me/sub-users",
            new { Username = "fresh", Email = "fresh@example.com", Features = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, invitation.StatusCode);
    }

    /// <summary>
    /// The owner cancels an open offer by removing the pending row: the offer closes, the person can no
    /// longer accept it, and another account's owner is told 404 and leaves it untouched.
    /// </summary>
    [Fact]
    public async Task The_owner_cancels_an_open_offer_and_another_accounts_owner_cannot()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var rival = await EndpointHarness.SignInAsync(factory, "owner-b");
        using var invitee = await EndpointHarness.SignInAsync(factory, "pat");
        await OfferAsync(owner, "pat");
        var offerId = await OfferIdAsync(invitee);
        using var listed = JsonDocument.Parse(await owner.GetStringAsync("/api/v1/me/sub-users"));
        var pending = Assert.Single(listed.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(pending.GetProperty("isOffer").GetBoolean());
        var rowId = pending.GetProperty("id").GetGuid();
        Assert.Equal(offerId, rowId);

        var byRival = await rival.DeleteAsync($"/api/v1/me/sub-users/{rowId}");
        Assert.Equal(HttpStatusCode.NotFound, byRival.StatusCode);
        Assert.Equal(1, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));

        var cancelled = await owner.DeleteAsync($"/api/v1/me/sub-users/{rowId}");

        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.Equal(0, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
        var accepted = await invitee.PostAsync($"/api/v1/me/membership-offers/{offerId}/accept", null);
        Assert.Equal(HttpStatusCode.NotFound, accepted.StatusCode);
        Assert.Equal(0, await SubUserTeamWorld.MembershipCountAsync(factory, "pat", "acme"));
        using var list = JsonDocument.Parse(await owner.GetStringAsync("/api/v1/me/sub-users"));
        Assert.Empty(list.RootElement.GetProperty("items").EnumerateArray());
    }

    /// <summary>The owner's list holds members and open offers together with a correct total and the states of each.</summary>
    [Fact]
    public async Task The_list_counts_members_and_open_offers_together()
    {
        await using var factory = await BootAsync();
        await SubUserTeamWorld.SeedMemberAsync(factory, "deploy", AccountFeature.Databases);
        await SubUserTeamWorld.SeedMemberAsync(factory, "build", AccountFeature.Cron);
        await SubUserTeamWorld.SeedAccountlessCustomerAsync(factory, "newcomer");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        await OfferAsync(owner, "pat");
        await OfferAsync(owner, "newcomer");

        using var everyone = JsonDocument.Parse(await owner.GetStringAsync("/api/v1/me/sub-users?pageSize=10"));
        using var searched = JsonDocument.Parse(await owner.GetStringAsync("/api/v1/me/sub-users?search=pat"));

        Assert.Equal(4, everyone.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(4, everyone.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(
            2,
            everyone.RootElement.GetProperty("items").EnumerateArray()
                .Count(item => { return item.GetProperty("state").GetString() == "invited"; }));
        Assert.Equal(
            2,
            everyone.RootElement.GetProperty("items").EnumerateArray()
                .Count(item => { return item.GetProperty("isOffer").GetBoolean(); }));
        Assert.Equal(1, searched.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// An offer, whether it was honoured or withheld, leaves one entry naming the owner, and the entry
    /// records the real outcome: succeeded when an offer was stored, failed when none was.
    /// </summary>
    [Fact]
    public async Task Every_offer_is_audited_with_its_real_outcome()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        await OfferAsync(owner, "pat");
        await OfferAsync(owner, "ghost");

        var entries = await SubUserTeamWorld.AuditEntriesAsync(factory, IdentityAuditActions.SubUserMembershipOffered);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => { Assert.Equal("owner-a", entry.ActorUsername); });
        Assert.Equal([true, false], entries.Select(entry => { return entry.Succeeded; }));
    }

    /// <summary>Posts an offer.</summary>
    /// <param name="client">The caller.</param>
    /// <param name="identifier">What the caller typed: a login name or an address.</param>
    /// <returns>The response.</returns>
    private static Task<HttpResponseMessage> OfferAsync(HttpClient client, string identifier)
    {
        return client.PostAsJsonAsync(Endpoint, new { Identifier = identifier, Features = DatabasesFeatures });
    }

    /// <summary>Reads the id of the one offer addressed to the signed-in invitee.</summary>
    /// <param name="invitee">A client signed in as the invitee.</param>
    /// <returns>The offer's id.</returns>
    private static async Task<Guid> OfferIdAsync(HttpClient invitee)
    {
        using var offers = JsonDocument.Parse(await invitee.GetStringAsync("/api/v1/me/membership-offers"));
        return Assert.Single(offers.RootElement.EnumerateArray()).GetProperty("id").GetGuid();
    }

    /// <summary>The names of the properties of a JSON response body, in order.</summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The top-level property names.</returns>
    private static string[] PropertyNames(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        return body.RootElement.EnumerateObject().Select(property => { return property.Name; }).ToArray();
    }

    /// <summary>Boots the standard panel with extra settings.</summary>
    /// <param name="configure">Extra host settings a test needs, or <c>null</c>.</param>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private Task<WebApplicationFactory<Program>> BootAsync(Action<IWebHostBuilder>? configure = null)
    {
        return SubUserTeamWorld.BootAsync(_pg, _mailer, configure: configure);
    }
}
