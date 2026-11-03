using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// An account's owner sends a team member's invitation again over real HTTP (issue #328): the address is
/// the one already stored, a new link replaces the old one, and every way of aiming the action at
/// somebody else's team is refused. Threat note:
/// docs/superpowers/notes/2026-11-03-membership-offers-threat-note.md.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: the mail transport itself (the recording mailer stands in for the SMTP server).
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SubUserResendEndpointTests : IAsyncLifetime
{
    /// <summary>The grant list naming only the databases feature.</summary>
    private static readonly string[] DatabasesFeatures = { "databases" };

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The mail server's double: records what the panel mails.</summary>
    private readonly RecordingMailer _mailer = new(TimeSpan.Zero);

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SubUserResendEndpointTests(PostgresFixture postgres)
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
    /// Resending mails the address stored on the login a new link, the previous link stops working, and
    /// the new one lets the person accept.
    /// </summary>
    [Fact]
    public async Task Resending_mails_the_stored_address_a_new_link_and_retires_the_old_one()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        await InviteAsync(owner, "deploy");
        var first = await SubUserTeamWorld.MailToAsync(_mailer, "deploy@example.com");
        var id = await SubUserTeamWorld.UserIdAsync(factory, "deploy");

        var resent = await owner.PostAsync($"/api/v1/me/sub-users/{id}/invitation", null);

        Assert.Equal(HttpStatusCode.NoContent, resent.StatusCode);
        var second = await SubUserTeamWorld.MailToAsync(_mailer, "deploy@example.com", skip: 1);
        var oldToken = SubUserTeamWorld.TokenIn(first);
        var newToken = SubUserTeamWorld.TokenIn(second);
        Assert.NotEqual(oldToken, newToken);
        using var anonymous = factory.CreateClient();
        var withOld = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = oldToken, NewPassword = EndpointHarness.Password });
        Assert.NotEqual(HttpStatusCode.OK, withOld.StatusCode);
        var withNew = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = newToken, NewPassword = EndpointHarness.Password });
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
        Assert.All(_mailer.Sent, mail => { Assert.Equal("deploy@example.com", mail.Recipient); });
    }

    /// <summary>A team member who has already accepted cannot be sent an invitation: 409, and nothing is mailed.</summary>
    [Fact]
    public async Task Resending_to_a_member_who_already_accepted_is_a_conflict_and_mails_nobody()
    {
        await using var factory = await BootAsync();
        var memberId = await SubUserTeamWorld.SeedMemberAsync(factory, "deploy", AccountFeature.Databases);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var resent = await owner.PostAsync($"/api/v1/me/sub-users/{memberId}/invitation", null);

        Assert.Equal(HttpStatusCode.Conflict, resent.StatusCode);
        using var body = JsonDocument.Parse(await resent.Content.ReadAsStringAsync());
        Assert.Equal("SubUserNotPending", body.RootElement.GetProperty("code").GetString());
        Assert.Empty(_mailer.Sent);
    }

    /// <summary>
    /// Another account's owner, and an id nobody holds, get the same 404, and the invited person is sent
    /// nothing: the account is never a parameter, so there is nothing to aim at another team.
    /// </summary>
    [Fact]
    public async Task Another_accounts_owner_and_an_unknown_id_both_get_404_and_nothing_is_mailed()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        await InviteAsync(owner, "deploy");
        await SubUserTeamWorld.MailToAsync(_mailer, "deploy@example.com");
        var id = await SubUserTeamWorld.UserIdAsync(factory, "deploy");
        using var rival = await EndpointHarness.SignInAsync(factory, "owner-b");

        var foreign = await rival.PostAsync($"/api/v1/me/sub-users/{id}/invitation", null);
        var unknown = await rival.PostAsync($"/api/v1/me/sub-users/{Guid.NewGuid()}/invitation", null);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var foreignBody = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync());
        using var unknownBody = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        Assert.Equal("SubUserNotFound", foreignBody.RootElement.GetProperty("code").GetString());
        Assert.Equal("SubUserNotFound", unknownBody.RootElement.GetProperty("code").GetString());
        Assert.Single(_mailer.Sent);
    }

    /// <summary>A team member, an administrator and an anonymous caller cannot resend an invitation.</summary>
    [Fact]
    public async Task Only_the_owner_can_resend()
    {
        await using var factory = await BootAsync();
        var memberId = await SubUserTeamWorld.SeedMemberAsync(factory, "deploy", AccountFeature.All);
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var anonymous = factory.CreateClient();
        var path = $"/api/v1/me/sub-users/{memberId}/invitation";

        var asMember = await member.PostAsync(path, null);
        var asAdmin = await admin.PostAsync(path, null);
        var asAnonymous = await anonymous.PostAsync(path, null);

        Assert.Equal(HttpStatusCode.Forbidden, asMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
        using var body = JsonDocument.Parse(await asMember.Content.ReadAsStringAsync());
        Assert.Equal("SubUsersOwnerOnly", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>An operator viewing as the owner is refused with the view-as code, and nothing is mailed.</summary>
    [Fact]
    public async Task A_view_as_token_cannot_resend_an_invitation()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        await InviteAsync(owner, "deploy");
        await SubUserTeamWorld.MailToAsync(_mailer, "deploy@example.com");
        var id = await SubUserTeamWorld.UserIdAsync(factory, "deploy");
        using var viewing = await SubUserTeamWorld.StartViewingAsync(factory, "acme");

        var response = await viewing.PostAsync($"/api/v1/me/sub-users/{id}/invitation", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("HostViewAsNotAllowed", body.RootElement.GetProperty("code").GetString());
        Assert.Single(_mailer.Sent);
    }

    /// <summary>A suspended account's owner cannot resend an invitation.</summary>
    [Fact]
    public async Task A_suspended_account_cannot_resend_an_invitation()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        await InviteAsync(owner, "deploy");
        await SubUserTeamWorld.MailToAsync(_mailer, "deploy@example.com");
        var id = await SubUserTeamWorld.UserIdAsync(factory, "deploy");
        await SubUserTeamWorld.SuspendAccountAsync(factory, "acme");

        var response = await owner.PostAsync($"/api/v1/me/sub-users/{id}/invitation", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InvitationAccountSuspended", body.RootElement.GetProperty("code").GetString());
        Assert.Single(_mailer.Sent);
    }

    /// <summary>A resend spends the same per-account sending bucket as an invitation, so the two cannot be alternated past it.</summary>
    [Fact]
    public async Task Resending_shares_the_invitation_sending_rate_limit()
    {
        await using var factory = await BootAsync(builder =>
        {
            builder.UseSetting("RateLimiting:InvitationSendMaxRequests", "2");
        });
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        await InviteAsync(owner, "deploy");
        var id = await SubUserTeamWorld.UserIdAsync(factory, "deploy");

        var first = await owner.PostAsync($"/api/v1/me/sub-users/{id}/invitation", null);
        var second = await owner.PostAsync($"/api/v1/me/sub-users/{id}/invitation", null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    /// <summary>A resend leaves one succeeded journal entry naming the owner and the person, and it never holds the token.</summary>
    [Fact]
    public async Task A_resend_is_audited_without_the_token()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        await InviteAsync(owner, "deploy");
        await SubUserTeamWorld.MailToAsync(_mailer, "deploy@example.com");
        var id = await SubUserTeamWorld.UserIdAsync(factory, "deploy");

        await owner.PostAsync($"/api/v1/me/sub-users/{id}/invitation", null);
        var second = await SubUserTeamWorld.MailToAsync(_mailer, "deploy@example.com", skip: 1);
        await owner.PostAsync($"/api/v1/me/sub-users/{Guid.NewGuid()}/invitation", null);

        var entries = await SubUserTeamWorld.AuditEntriesAsync(factory, IdentityAuditActions.SubUserInvitationResent);
        Assert.Equal(2, entries.Count);
        Assert.Equal([true, false], entries.Select(entry => { return entry.Succeeded; }));
        Assert.All(entries, entry => { Assert.Equal("owner-a", entry.ActorUsername); });
        Assert.Equal("deploy", entries[0].Subject);
        var token = SubUserTeamWorld.TokenIn(second);
        Assert.All(entries, entry =>
        {
            Assert.DoesNotContain(token, entry.Subject, StringComparison.Ordinal);
            Assert.DoesNotContain(token, entry.UserAgent, StringComparison.Ordinal);
        });
    }

    /// <summary>Boots the standard panel with extra settings.</summary>
    /// <param name="configure">Extra host settings a test needs, or <c>null</c>.</param>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private Task<WebApplicationFactory<Program>> BootAsync(Action<IWebHostBuilder>? configure = null)
    {
        return SubUserTeamWorld.BootAsync(_pg, _mailer, configure: configure);
    }

    /// <summary>Invites a person to the owner's account and expects the invitation to be accepted for sending.</summary>
    /// <param name="owner">A client signed in as the owner.</param>
    /// <param name="username">The new person's login name; their address is <c>&lt;username&gt;@example.com</c>.</param>
    private static async Task InviteAsync(HttpClient owner, string username)
    {
        var invited = await owner.PostAsJsonAsync(
            "/api/v1/me/sub-users",
            new { Username = username, Email = $"{username}@example.com", Features = DatabasesFeatures });
        Assert.Equal(HttpStatusCode.Accepted, invited.StatusCode);
    }
}
