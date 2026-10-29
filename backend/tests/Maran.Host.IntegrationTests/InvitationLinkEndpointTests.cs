using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The administrator's invitation link over real HTTP against a real PostgreSQL, asserting the claims
/// of its threat note (<c>docs/superpowers/notes/2026-10-26-invitation-link-threat-note.md</c>) on the
/// wire and at rest: administrator-only (with the inverse control that the administrator IS served),
/// uncacheable, re-minted rather than recovered, the previous link dead, and the plaintext found in no
/// column of any table — proved with a probe that is first shown able to find a planted value.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class InvitationLinkEndpointTests : IAsyncLifetime
{
    /// <summary>The plaintext the seeded, already-mailed invitation carries.</summary>
    private const string MailedToken = "the-token-that-was-mailed-before";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public InvitationLinkEndpointTests(PostgresFixture postgres)
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

    /// <summary>A customer is refused both invitation routes with 403, whichever account they name.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task A_customer_is_refused_both_invitation_routes_with_forbidden_whichever_account_they_name(
        bool post,
        bool ownAccount)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, ownAccountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        var (_, strangerAccountId) = await EndpointHarness.SeedAccountAsync(factory, "pepper");
        await EndpointHarness.SeedCustomerAsync(factory, "olive", ownAccountId);
        using var client = await EndpointHarness.SignInAsync(factory, "olive");
        var target = ownAccount ? ownAccountId : strangerAccountId;

        var response = post
            ? await client.PostAsync($"/api/v1/invitations/{target}/link", content: null)
            : await client.GetAsync($"/api/v1/invitations/{target}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An anonymous caller is refused both invitation routes with 401.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_anonymous_caller_is_refused_both_invitation_routes_with_unauthorized(bool post)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        using var client = factory.CreateClient();

        var response = post
            ? await client.PostAsync($"/api/v1/invitations/{accountId}/link", content: null)
            : await client.GetAsync($"/api/v1/invitations/{accountId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>The administrator is served a link, and it is marked uncacheable on the wire.</summary>
    [Fact]
    public async Task The_administrator_is_served_a_link_and_it_is_marked_uncacheable_on_the_wire()
    {
        // The inverse control for the two refusals above: a gate that refused everybody would pass
        // them, and this is the half that shows the door opens for the one caller it is meant for.
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedAdministratorAsync(factory);
        await SeedInvitedLoginAsync(factory, accountId);
        using var client = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await client.PostAsync($"/api/v1/invitations/{accountId}/link", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith("/accept-invitation?token=", body.RootElement.GetProperty("link").GetString(), StringComparison.Ordinal);
        Assert.True(body.RootElement.TryGetProperty("expiresAt", out _));
    }

    /// <summary>The issued link lets the customer set a password and sign in, and works once.</summary>
    [Fact]
    public async Task The_issued_link_lets_the_customer_set_a_password_and_sign_in_and_works_once()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedAdministratorAsync(factory);
        await SeedInvitedLoginAsync(factory, accountId);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var token = await IssueLinkTokenAsync(admin, accountId);
        using var anonymous = factory.CreateClient();

        var accepted = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = token, NewPassword = EndpointHarness.Password });
        var login = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "owner-olive", Password = EndpointHarness.Password });
        var replay = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = token, NewPassword = "another password entirely" });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    /// <summary>Issuing a link kills the previous one and the one that was mailed, and only the newest works.</summary>
    [Fact]
    public async Task Issuing_a_link_kills_the_previous_one_and_the_one_that_was_mailed_and_only_the_newest_works()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedAdministratorAsync(factory);
        await SeedInvitedLoginAsync(factory, accountId);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var first = await IssueLinkTokenAsync(admin, accountId);
        var second = await IssueLinkTokenAsync(admin, accountId);
        using var anonymous = factory.CreateClient();

        var mailedAttempt = await Accept(anonymous, MailedToken);
        var firstAttempt = await Accept(anonymous, first);
        var secondAttempt = await Accept(anonymous, second);

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, mailedAttempt);
        Assert.Equal(HttpStatusCode.BadRequest, firstAttempt);
        Assert.Equal(HttpStatusCode.OK, secondAttempt);
    }

    /// <summary>The plaintext token is found in no column of any table, and the probe is shown able to find one.</summary>
    [Fact]
    public async Task The_plaintext_token_is_found_in_no_column_of_any_table_and_the_probe_is_shown_able_to_find_one()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedAdministratorAsync(factory);
        await SeedInvitedLoginAsync(factory, accountId);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var token = await IssueLinkTokenAsync(admin, accountId);

        // Positive control: the same probe, the same database, a value planted where it must be found.
        await PlaintextProbe.PlantControlAsync(_pg.GetConnectionString(), token);
        var withControl = await PlaintextProbe.FindAsync(_pg.GetConnectionString(), token);
        Assert.Equal(["public.ProbeControl.AsBytes", "public.ProbeControl.AsText"], withControl.Order(StringComparer.Ordinal));

        // The same hunt over everything else: the one hit is the planted table and nothing the panel wrote.
        var panelHits = (await PlaintextProbe.FindAsync(_pg.GetConnectionString(), token))
            .Where(hit =>
            {
                return !hit.StartsWith("public.ProbeControl.", StringComparison.Ordinal);
            })
            .ToList();
        Assert.Empty(panelHits);

        // And the digest, which a store may hold, is there: the probe's silence is not an empty database.
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Contains(
            await identity.InvitationTokens.AsNoTracking().ToListAsync(),
            row =>
            {
                return row.TokenHash == InvitationTokenHasher.Hash(token);
            });
    }

    /// <summary>Issuing a link is journalled with the administrator, the customer and no part of the link.</summary>
    [Fact]
    public async Task Issuing_a_link_is_journalled_with_the_administrator_the_customer_and_no_part_of_the_link()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedAdministratorAsync(factory);
        await SeedInvitedLoginAsync(factory, accountId);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var token = await IssueLinkTokenAsync(admin, accountId);

        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entry = Assert.Single(
            await identity.AuditEvents.AsNoTracking().Where(e => e.Action == AuditActions.InvitationLinkIssued).ToListAsync());
        Assert.Equal("admin", entry.ActorUsername);
        Assert.Equal("owner-olive", entry.Subject);
        Assert.True(entry.Succeeded);
        foreach (var field in new[] { entry.Subject, entry.ActorUsername, entry.IpAddress, entry.UserAgent })
        {
            Assert.DoesNotContain(token, field, StringComparison.Ordinal);
        }
    }

    /// <summary>A login that already has a password gets no link, and the refusal is journalled.</summary>
    [Fact]
    public async Task A_login_that_already_has_a_password_gets_no_link_and_the_refusal_is_journalled()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedAdministratorAsync(factory);
        await EndpointHarness.SeedCustomerAsync(factory, "olive", accountId);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.PostAsync($"/api/v1/invitations/{accountId}/link", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Empty(await identity.InvitationTokens.ToListAsync());
        var entry = Assert.Single(
            await identity.AuditEvents.AsNoTracking().Where(e => e.Action == AuditActions.InvitationLinkIssued).ToListAsync());
        Assert.False(entry.Succeeded);
    }

    /// <summary>An account that does not exist is answered 404.</summary>
    [Fact]
    public async Task An_account_that_does_not_exist_is_answered_not_found()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.PostAsync($"/api/v1/invitations/{Guid.NewGuid()}/link", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The status follows the invitation through none, pending, expired and accepted.</summary>
    [Fact]
    public async Task The_status_follows_the_invitation_through_none_pending_expired_and_accepted()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedAdministratorAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var none = await StatusAsync(admin, accountId);

        var userId = await SeedInvitedLoginAsync(factory, accountId);
        var pending = await StatusAsync(admin, accountId);

        await ExpireTokensAsync(factory, userId);
        var expired = await StatusAsync(admin, accountId);

        var token = await IssueLinkTokenAsync(admin, accountId);
        var reissued = await StatusAsync(admin, accountId);

        using var anonymous = factory.CreateClient();
        await Accept(anonymous, token);
        var accepted = await StatusAsync(admin, accountId);

        Assert.Equal("none", none.State);
        Assert.Equal("pending", pending.State);
        Assert.NotNull(pending.ExpiresAt);
        Assert.Equal("expired", expired.State);
        Assert.Null(expired.ExpiresAt);
        Assert.Equal("pending", reissued.State);
        Assert.Equal("accepted", accepted.State);
        Assert.Null(accepted.ExpiresAt);
    }

    /// <summary>The status answer carries no token and no link.</summary>
    [Fact]
    public async Task The_status_answer_carries_no_token_and_no_link()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedAdministratorAsync(factory);
        await SeedInvitedLoginAsync(factory, accountId);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync($"/api/v1/invitations/{accountId}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["expiresAt", "state", "stateDisplayName"], body.RootElement.EnumerateObject().Select(property =>
        {
            return property.Name;
        }).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(MailedToken, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>Stores an invited login for the account, carrying the already-mailed token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The account the login belongs to.</param>
    /// <returns>The login's identity.</returns>
    private static async Task<Guid> SeedInvitedLoginAsync(WebApplicationFactory<Program> factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var invited = User.Invite(Guid.NewGuid(), "owner-olive", "owner-olive@example.com", accountId, now);
        context.Users.Add(invited);
        context.InvitationTokens.Add(new InvitationToken(Guid.NewGuid(), invited.Id, InvitationTokenHasher.Hash(MailedToken), now));
        await context.SaveChangesAsync();

        return invited.Id;
    }

    /// <summary>Ages every token of the login past its lifetime, as a week without use would.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="userId">The login whose tokens lapse.</param>
    private static async Task ExpireTokensAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        foreach (var token in await context.InvitationTokens.Where(candidate => candidate.UserId == userId).ToListAsync())
        {
            // Through EF's metadata, because the entity's setters are private and the lapse of a week
            // is not something a test can wait for.
            context.Entry(token).Property(nameof(InvitationToken.ExpiresAt)).CurrentValue = now - TimeSpan.FromMinutes(1);
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Asks for a link and returns the plaintext token inside it.</summary>
    /// <param name="admin">A client signed in as the administrator.</param>
    /// <param name="accountId">The account to invite.</param>
    private static async Task<string> IssueLinkTokenAsync(HttpClient admin, Guid accountId)
    {
        var response = await admin.PostAsync($"/api/v1/invitations/{accountId}/link", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var link = body.RootElement.GetProperty("link").GetString()!;
        const string Marker = "token=";
        return Uri.UnescapeDataString(link[(link.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..]);
    }

    /// <summary>Spends <paramref name="token"/> as the customer who received it would.</summary>
    /// <param name="anonymous">A client carrying no credentials.</param>
    /// <param name="token">The plaintext token.</param>
    private static async Task<HttpStatusCode> Accept(HttpClient anonymous, string token)
    {
        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = token, NewPassword = EndpointHarness.Password });
        return response.StatusCode;
    }

    /// <summary>Reads the invitation status of an account.</summary>
    /// <param name="admin">A client signed in as the administrator.</param>
    /// <param name="accountId">The account asked about.</param>
    private static async Task<(string State, string? ExpiresAt)> StatusAsync(HttpClient admin, Guid accountId)
    {
        var response = await admin.GetAsync($"/api/v1/invitations/{accountId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var expires = body.RootElement.GetProperty("expiresAt");
        return (
            body.RootElement.GetProperty("state").GetString()!,
            expires.ValueKind == JsonValueKind.Null ? null : expires.GetString());
    }
}
