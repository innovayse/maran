using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The invited person's side of an offer to join an account, over real HTTP (issue #328): read the
/// offers addressed to them, accept or decline one, and the ways an offer must NOT work: another login's
/// offer, an expired one, one already resolved, a suspended account, and a view-as token. Threat note:
/// docs/superpowers/notes/2026-11-03-membership-offers-threat-note.md.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class MembershipOffersEndpointTests : IAsyncLifetime
{
    /// <summary>The route the invitee reads and answers offers on.</summary>
    private const string Endpoint = "/api/v1/me/membership-offers";

    /// <summary>The grant list naming only the databases feature.</summary>
    private static readonly string[] DatabasesFeatures = { "databases" };

    /// <summary>The grant list naming only the file transfer feature.</summary>
    private static readonly string[] FileTransferFeatures = { "fileTransfer" };

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The mail server's double: records what the panel mails.</summary>
    private readonly RecordingMailer _mailer = new(TimeSpan.Zero);

    /// <summary>The panel clock, moved past an offer's lifetime by the expiry test.</summary>
    private readonly AdvancingClock _clock = new();

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MembershipOffersEndpointTests(PostgresFixture postgres)
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
    /// The whole path: the owner offers, the person who has no account reads the offer naming the account
    /// and the features, accepts, and then holds exactly those features and no others.
    /// </summary>
    [Fact]
    public async Task An_offer_is_read_accepted_and_the_new_member_holds_exactly_the_offered_features()
    {
        await using var factory = await BootAsync();
        await SubUserTeamWorld.SeedAccountlessCustomerAsync(factory, "newcomer");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var invitee = await EndpointHarness.SignInAsync(factory, "newcomer");
        await OfferAsync(owner, "newcomer", DatabasesFeatures);

        using var offers = JsonDocument.Parse(await invitee.GetStringAsync(Endpoint));
        var offer = Assert.Single(offers.RootElement.EnumerateArray());
        Assert.Equal("acme", offer.GetProperty("accountName").GetString());
        var feature = Assert.Single(offer.GetProperty("features").EnumerateArray());
        Assert.Equal("databases", feature.GetProperty("feature").GetString());
        Assert.False(string.IsNullOrWhiteSpace(feature.GetProperty("featureDisplayName").GetString()));
        Assert.True(offer.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        var offerId = offer.GetProperty("id").GetGuid();

        var accepted = await invitee.PostAsync($"{Endpoint}/{offerId}/accept", null);

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(1, await SubUserTeamWorld.MembershipCountAsync(factory, "newcomer", "acme"));
        Assert.Equal(0, await SubUserTeamWorld.OpenOfferCountAsync(factory, "newcomer", "acme"));
        using var member = await EndpointHarness.SignInAsync(factory, "newcomer");
        using var profile = JsonDocument.Parse(await member.GetStringAsync("/api/v1/profile"));
        var access = profile.RootElement.GetProperty("accountAccess");
        Assert.Equal("member", access.GetProperty("role").GetString());
        Assert.Equal("databases", Assert.Single(access.GetProperty("features").EnumerateArray()).GetString());
        var refused = await member.GetAsync("/api/v1/sftp-users");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var refusedBody = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("AccountFeatureNotGranted", refusedBody.RootElement.GetProperty("code").GetString());
        using var after = JsonDocument.Parse(await member.GetStringAsync(Endpoint));
        Assert.Empty(after.RootElement.EnumerateArray());
    }

    /// <summary>The inverse control: a member whose accepted offer included the feature reaches it.</summary>
    [Fact]
    public async Task A_member_whose_offer_included_a_feature_reaches_it()
    {
        await using var factory = await BootAsync();
        await SubUserTeamWorld.SeedAccountlessCustomerAsync(factory, "newcomer");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var invitee = await EndpointHarness.SignInAsync(factory, "newcomer");
        await OfferAsync(owner, "newcomer", FileTransferFeatures);

        await invitee.PostAsync($"{Endpoint}/{await OfferIdAsync(invitee)}/accept", null);

        using var member = await EndpointHarness.SignInAsync(factory, "newcomer");
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/sftp-users")).StatusCode);
    }

    /// <summary>
    /// A person who already owns an account keeps it: accepting adds one member row for the offering
    /// account holding the offered grants, and exactly one, even when the answer is sent twice.
    /// </summary>
    [Fact]
    public async Task Accepting_adds_one_member_row_with_the_offered_grants_and_a_second_accept_is_404()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var invitee = await EndpointHarness.SignInAsync(factory, "pat");
        await OfferAsync(owner, "pat", DatabasesFeatures);
        var offerId = await OfferIdAsync(invitee);

        var first = await invitee.PostAsync($"{Endpoint}/{offerId}/accept", null);
        var second = await invitee.PostAsync($"{Endpoint}/{offerId}/accept", null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("MembershipOfferNotFound", body.RootElement.GetProperty("code").GetString());
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var patId = await SubUserTeamWorld.UserIdAsync(factory, "pat");
        var acme = await SubUserTeamWorld.AccountIdAsync(factory, "acme");
        var membership = await identity.AccountMemberships.AsNoTracking()
            .SingleAsync(row => row.UserId == patId && row.AccountId == acme);
        Assert.Equal(AccountFeature.Databases, membership.Grants);
        // The person's own account is untouched: the owner row of gamma and the member row of acme.
        Assert.Equal(2, await identity.AccountMemberships.CountAsync(row => row.UserId == patId));
    }

    /// <summary>Declining closes the offer, adds nobody, and a declined offer can no longer be accepted.</summary>
    [Fact]
    public async Task Declining_closes_the_offer_and_it_can_no_longer_be_accepted()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var invitee = await EndpointHarness.SignInAsync(factory, "pat");
        await OfferAsync(owner, "pat", DatabasesFeatures);
        var offerId = await OfferIdAsync(invitee);

        var declined = await invitee.PostAsync($"{Endpoint}/{offerId}/decline", null);
        var accepted = await invitee.PostAsync($"{Endpoint}/{offerId}/accept", null);

        Assert.Equal(HttpStatusCode.NoContent, declined.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, accepted.StatusCode);
        Assert.Equal(0, await SubUserTeamWorld.MembershipCountAsync(factory, "pat", "acme"));
        Assert.Equal(0, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
        using var list = JsonDocument.Parse(await invitee.GetStringAsync(Endpoint));
        Assert.Empty(list.RootElement.EnumerateArray());
    }

    /// <summary>
    /// An offer that passed its lifetime reads as absent: it is not listed, accepting it is 404 (not a
    /// conflict that would say it once existed), and the owner can offer again and the person sees exactly one offer.
    /// </summary>
    [Fact]
    public async Task An_expired_offer_reads_as_absent_and_a_fresh_one_can_follow_it()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var invitee = await EndpointHarness.SignInAsync(factory, "pat");
        await OfferAsync(owner, "pat", DatabasesFeatures);
        var expiredId = await OfferIdAsync(invitee);

        _clock.Advance(TimeSpan.FromDays(15));

        using var listed = JsonDocument.Parse(await invitee.GetStringAsync(Endpoint));
        Assert.Empty(listed.RootElement.EnumerateArray());
        var accepted = await invitee.PostAsync($"{Endpoint}/{expiredId}/accept", null);
        Assert.Equal(HttpStatusCode.NotFound, accepted.StatusCode);
        Assert.Equal(0, await SubUserTeamWorld.MembershipCountAsync(factory, "pat", "acme"));

        var again = await OfferAsync(owner, "pat", DatabasesFeatures);

        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        var freshId = await OfferIdAsync(invitee);
        Assert.NotEqual(expiredId, freshId);
        Assert.Equal(1, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
        await SubUserTeamWorld.MailToAsync(_mailer, "pat@example.com", skip: 1);
        Assert.Equal(HttpStatusCode.NoContent, (await invitee.PostAsync($"{Endpoint}/{freshId}/accept", null)).StatusCode);
    }

    /// <summary>
    /// A login the offer was not addressed to cannot read, accept or decline it: an unknown id and a
    /// foreign id are the same 404, and the offer stays open for the person it was meant for.
    /// </summary>
    [Fact]
    public async Task Another_login_cannot_read_accept_or_decline_an_offer_and_gets_the_same_404_as_for_an_unknown_id()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var invitee = await EndpointHarness.SignInAsync(factory, "pat");
        using var rival = await EndpointHarness.SignInAsync(factory, "owner-b");
        await OfferAsync(owner, "pat", DatabasesFeatures);
        var offerId = await OfferIdAsync(invitee);

        var accept = await rival.PostAsync($"{Endpoint}/{offerId}/accept", null);
        var decline = await rival.PostAsync($"{Endpoint}/{offerId}/decline", null);
        var unknown = await rival.PostAsync($"{Endpoint}/{Guid.NewGuid()}/accept", null);
        using var rivalList = JsonDocument.Parse(await rival.GetStringAsync(Endpoint));

        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, decline.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Empty(rivalList.RootElement.EnumerateArray());
        foreach (var refused in new[] { accept, decline, unknown })
        {
            using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
            Assert.Equal("MembershipOfferNotFound", body.RootElement.GetProperty("code").GetString());
        }

        Assert.Equal(1, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
        Assert.Equal(0, await SubUserTeamWorld.MembershipCountAsync(factory, "owner-b", "acme"));
    }

    /// <summary>An anonymous caller cannot read or answer offers.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        await using var factory = await BootAsync();
        using var anonymous = factory.CreateClient();
        var path = $"{Endpoint}/{Guid.NewGuid()}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"{path}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"{path}/decline", null)).StatusCode);
    }

    /// <summary>
    /// An operator viewing as the invited person is refused every action with the view-as code, so
    /// nobody can consent on the person's behalf, and the offer stays open.
    /// </summary>
    [Fact]
    public async Task A_view_as_token_cannot_read_accept_or_decline_an_offer()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var invitee = await EndpointHarness.SignInAsync(factory, "pat");
        await OfferAsync(owner, "pat", DatabasesFeatures);
        var offerId = await OfferIdAsync(invitee);
        using var viewing = await SubUserTeamWorld.StartViewingAsync(factory, "gamma");

        var list = await viewing.GetAsync(Endpoint);
        var accept = await viewing.PostAsync($"{Endpoint}/{offerId}/accept", null);
        var decline = await viewing.PostAsync($"{Endpoint}/{offerId}/decline", null);

        foreach (var refused in new[] { list, accept, decline })
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
            Assert.Equal("HostViewAsNotAllowed", body.RootElement.GetProperty("code").GetString());
        }

        Assert.Equal(1, await SubUserTeamWorld.OpenOfferCountAsync(factory, "pat", "acme"));
        Assert.Equal(0, await SubUserTeamWorld.MembershipCountAsync(factory, "pat", "acme"));
    }

    /// <summary>An account suspended after the offer was made takes no new member: accepting is a conflict and adds nobody.</summary>
    [Fact]
    public async Task Accepting_for_an_account_suspended_since_the_offer_is_refused()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var invitee = await EndpointHarness.SignInAsync(factory, "pat");
        await OfferAsync(owner, "pat", DatabasesFeatures);
        var offerId = await OfferIdAsync(invitee);
        await SubUserTeamWorld.SuspendAccountAsync(factory, "acme");

        var accepted = await invitee.PostAsync($"{Endpoint}/{offerId}/accept", null);

        Assert.Equal(HttpStatusCode.Conflict, accepted.StatusCode);
        using var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        Assert.Equal("MembershipOfferAccountSuspended", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, await SubUserTeamWorld.MembershipCountAsync(factory, "pat", "acme"));
    }

    /// <summary>
    /// A person who became a member of the account some other way before accepting does not get a second
    /// membership: the answer is not a success and the single membership row stays single.
    /// </summary>
    [Fact]
    public async Task Accepting_when_already_a_member_adds_no_second_membership()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var invitee = await EndpointHarness.SignInAsync(factory, "pat");
        await OfferAsync(owner, "pat", DatabasesFeatures);
        var offerId = await OfferIdAsync(invitee);
        await AdmitAsync(factory, "pat", "acme");

        var accepted = await invitee.PostAsync($"{Endpoint}/{offerId}/accept", null);

        Assert.NotEqual(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, accepted.StatusCode);
        Assert.Equal(1, await SubUserTeamWorld.MembershipCountAsync(factory, "pat", "acme"));
    }

    /// <summary>A customer who belongs to no account reads an empty list and is not a server error.</summary>
    [Fact]
    public async Task A_customer_with_no_account_reads_an_empty_list()
    {
        await using var factory = await BootAsync();
        await SubUserTeamWorld.SeedAccountlessCustomerAsync(factory, "newcomer");
        using var invitee = await EndpointHarness.SignInAsync(factory, "newcomer");

        var response = await invitee.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(body.RootElement.EnumerateArray());
    }

    /// <summary>
    /// Accepting and declining each leave one entry naming the person who answered, and a refused answer
    /// leaves a failed one.
    /// </summary>
    [Fact]
    public async Task Accepting_and_declining_are_audited()
    {
        await using var factory = await BootAsync();
        await SubUserTeamWorld.SeedAccountlessCustomerAsync(factory, "newcomer");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var invitee = await EndpointHarness.SignInAsync(factory, "newcomer");
        using var other = await EndpointHarness.SignInAsync(factory, "pat");
        await OfferAsync(owner, "newcomer", DatabasesFeatures);
        await OfferAsync(owner, "pat", DatabasesFeatures);
        var accepted = await OfferIdAsync(invitee);
        var declined = await OfferIdAsync(other);

        await invitee.PostAsync($"{Endpoint}/{accepted}/accept", null);
        await other.PostAsync($"{Endpoint}/{declined}/decline", null);
        await invitee.PostAsync($"{Endpoint}/{accepted}/accept", null);

        var acceptEntries = await SubUserTeamWorld.AuditEntriesAsync(
            factory, IdentityAuditActions.SubUserMembershipOfferAccepted);
        var declineEntries = await SubUserTeamWorld.AuditEntriesAsync(
            factory, IdentityAuditActions.SubUserMembershipOfferDeclined);
        Assert.Equal([true, false], acceptEntries.Select(entry => { return entry.Succeeded; }));
        Assert.All(acceptEntries, entry => { Assert.Equal("newcomer", entry.ActorUsername); });
        var decline = Assert.Single(declineEntries);
        Assert.Equal("pat", decline.ActorUsername);
        Assert.True(decline.Succeeded);
    }

    /// <summary>Posts an offer as the owner and expects it to be accepted for processing.</summary>
    /// <param name="client">The owner's client.</param>
    /// <param name="identifier">What the owner typed.</param>
    /// <param name="features">The features to offer.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> OfferAsync(HttpClient client, string identifier, string[] features)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/sub-users/existing", new { Identifier = identifier, Features = features });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return response;
    }

    /// <summary>Reads the id of the one offer addressed to the signed-in invitee.</summary>
    /// <param name="invitee">A client signed in as the invitee.</param>
    /// <returns>The offer's id.</returns>
    private static async Task<Guid> OfferIdAsync(HttpClient invitee)
    {
        using var offers = JsonDocument.Parse(await invitee.GetStringAsync(Endpoint));
        return Assert.Single(offers.RootElement.EnumerateArray()).GetProperty("id").GetGuid();
    }

    /// <summary>Makes a login a team member of an account directly, as if it had joined another way.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login.</param>
    /// <param name="accountName">The account.</param>
    private static async Task AdmitAsync(WebApplicationFactory<Program> factory, string username, string accountName)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        identity.AccountMemberships.Add(AccountMembership.Admit(
            Guid.NewGuid(),
            await SubUserTeamWorld.UserIdAsync(factory, username),
            await SubUserTeamWorld.AccountIdAsync(factory, accountName),
            AccountFeature.None,
            now));
        await identity.SaveChangesAsync();
    }

    /// <summary>Boots the standard panel on the clock the expiry test moves.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private Task<WebApplicationFactory<Program>> BootAsync()
    {
        return SubUserTeamWorld.BootAsync(_pg, _mailer, _clock);
    }
}
