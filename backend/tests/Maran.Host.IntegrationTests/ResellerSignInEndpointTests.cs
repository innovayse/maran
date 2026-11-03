using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Notifications.Interfaces;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// A reseller's first sign-in over real HTTP and the real subscriber (issue #179): the administrator
/// creates the reseller, the panel invites its login by mail, the person accepts through the link, and
/// the access token it then holds names role Reseller and the reseller, and no account.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class ResellerSignInEndpointTests : IAsyncLifetime
{
    /// <summary>The features the plan created by the administrator turns on.</summary>
    private static readonly string[] PlanFeatures = { "sites", "ssl" };

    /// <summary>The address the invitation is sent to.</summary>
    private const string Recipient = "nova@example.com";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The mail server's double the invitation reaches.</summary>
    private readonly RecordingMailer _mailer = new(TimeSpan.Zero);

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerSignInEndpointTests(PostgresFixture postgres)
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
    /// A reseller the administrator creates signs in through the invitation link, and its access token is a
    /// reseller token: role Reseller, the reseller's id, no account, no view-as marker.
    /// </summary>
    [Fact]
    public async Task A_reseller_created_by_the_administrator_signs_in_through_the_invitation_link()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var resellerId = await CreateResellerAsync(admin);

        var mail = await SubUserTeamWorld.MailToAsync(_mailer, Recipient);
        var token = SubUserTeamWorld.TokenIn(mail);
        using var anonymous = factory.CreateClient();
        var accepted = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = token, NewPassword = EndpointHarness.Password });
        var signedIn = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "nova", Password = EndpointHarness.Password });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        using var body = JsonDocument.Parse(await signedIn.Content.ReadAsStringAsync());
        var session = body.RootElement.GetProperty("session");
        Assert.Equal("reseller", session.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("user").GetProperty("accountId").ValueKind);
        Assert.False(session.GetProperty("requiresTwoFactorSetup").GetBoolean());

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(session.GetProperty("accessToken").GetString()!);
        Assert.Equal(nameof(UserRole.Reseller), jwt.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(resellerId.ToString(), jwt.GetClaim(PanelClaimTypes.ResellerId).Value);
        Assert.False(jwt.TryGetClaim(PanelClaimTypes.AccountId, out _));
        Assert.False(jwt.TryGetClaim(PanelClaimTypes.SessionKind, out _));
        Assert.False(jwt.TryGetClaim(PanelClaimTypes.Actor, out _));
    }

    /// <summary>The login the panel created answers the reseller's own endpoints and the common profile.</summary>
    [Fact]
    public async Task The_new_reseller_reads_its_own_standing_and_profile()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var resellerId = await CreateResellerAsync(admin);
        await AcceptAsync(factory);

        using var reseller = await EndpointHarness.SignInAsync(factory, "nova");
        using var me = JsonDocument.Parse(await reseller.GetStringAsync("/api/v1/reseller/me"));
        using var profile = JsonDocument.Parse(await reseller.GetStringAsync("/api/v1/profile"));

        Assert.Equal(resellerId, me.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("nova", me.RootElement.GetProperty("name").GetString());
        Assert.Equal("reseller", profile.RootElement.GetProperty("role").GetString());
    }

    /// <summary>Until the link is followed the login cannot sign in, and the refusal is the one a wrong password gets.</summary>
    [Fact]
    public async Task A_reseller_who_has_not_accepted_the_invitation_cannot_sign_in()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        await CreateResellerAsync(admin);
        await SubUserTeamWorld.MailToAsync(_mailer, Recipient);
        using var anonymous = factory.CreateClient();

        var early = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "nova", Password = EndpointHarness.Password });
        var wrong = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "nova", Password = "wrong-password-entirely" });

        Assert.Equal(HttpStatusCode.Unauthorized, early.StatusCode);
        Assert.Equal(wrong.StatusCode, early.StatusCode);
        Assert.Equal(await CodeOfAsync(wrong), await CodeOfAsync(early));
    }

    /// <summary>The link works once: a second use is refused and the password the first use chose stands.</summary>
    [Fact]
    public async Task The_invitation_link_is_spent_by_its_first_use()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        await CreateResellerAsync(admin);
        var token = SubUserTeamWorld.TokenIn(await SubUserTeamWorld.MailToAsync(_mailer, Recipient));
        using var anonymous = factory.CreateClient();
        await anonymous.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = token, NewPassword = EndpointHarness.Password });

        var again = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = token, NewPassword = "another password entirely" });
        var signedIn = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "nova", Password = EndpointHarness.Password });

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("InvitationTokenInvalid", await CodeOfAsync(again));
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    /// <summary>The invitation of a reseller is journalled against the reseller's login.</summary>
    [Fact]
    public async Task The_invitation_of_a_reseller_is_journalled()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        await CreateResellerAsync(admin);
        await SubUserTeamWorld.MailToAsync(_mailer, Recipient);

        var entries = await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ResellerInvited);

        var entry = Assert.Single(entries);
        Assert.True(entry.Succeeded);
        Assert.Equal("nova", entry.Subject);
        Assert.Equal(await IdentityResellerWorld.UserIdAsync(factory, "nova"), entry.ActorUserId);
    }

    /// <summary>Boots the host with an administrator, a policy that does not force two-factor, and a panel address.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private Task<WebApplicationFactory<Program>> BootAsync()
    {
        return IdentityResellerWorld.BootAsync(
            _pg,
            forceTwoFactorForResellers: false,
            services =>
            {
                services.AddSingleton<IMailer>(_mailer);
            },
            panelUrl: "https://panel.example.com");
    }

    /// <summary>Creates the reseller <c>nova</c> through the administrator's endpoint.</summary>
    /// <param name="admin">The administrator's client.</param>
    /// <returns>The reseller's id.</returns>
    private static async Task<Guid> CreateResellerAsync(HttpClient admin)
    {
        var created = await admin.PostAsJsonAsync("/api/v1/resellers", new
        {
            name = "nova",
            contactEmail = Recipient,
            language = "en",
            envelope = new
            {
                maxAccounts = 5,
                diskPoolMb = 50_000,
                maxPlans = 5,
                maxDiskQuotaMb = 10_000,
                maxSites = 5,
                maxDatabases = 5,
                maxSftpUsers = 5,
                maxFtpUsers = 5,
                maxCronEntries = 5,
                maxPhpWorkersPerPool = 5,
                features = PlanFeatures,
                cpuQuotaPercent = new { kind = "unlimited" },
                memoryMaxMb = new { kind = "unlimited" },
                maxProcesses = new { kind = "unlimited" },
            },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Follows the invitation link the mail carried, choosing the standard password.</summary>
    /// <param name="factory">The booted host.</param>
    private async Task AcceptAsync(WebApplicationFactory<Program> factory)
    {
        var token = SubUserTeamWorld.TokenIn(await SubUserTeamWorld.MailToAsync(_mailer, Recipient));
        using var anonymous = factory.CreateClient();
        var accepted = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = token, NewPassword = EndpointHarness.Password });
        accepted.EnsureSuccessStatusCode();
    }

    /// <summary>Reads the machine code out of a problem answer.</summary>
    /// <param name="response">The refused answer.</param>
    /// <returns>Its <c>code</c>.</returns>
    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }
}
