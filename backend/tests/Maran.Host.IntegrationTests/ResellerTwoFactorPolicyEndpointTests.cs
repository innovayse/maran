using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Mandatory two-factor for resellers over real HTTP (issue #179, decision D4): a reseller can start
/// view-as into its customers, so with no stored policy its sign-in steers it to enrolment the way an
/// administrator's is steered, turning the policy's reseller switch off is honoured at the next
/// sign-in, and the reseller and administrator switches do not read each other.
/// </summary>
/// <remarks>
/// The walk of every governed route is in <c>TwoFactorEnrolmentSteeringTests</c>; this class holds what
/// is particular to the reseller switch: its default, its save through the administrator's endpoint, and
/// its independence.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ResellerTwoFactorPolicyEndpointTests : IAsyncLifetime
{
    /// <summary>The code a steered caller is refused with.</summary>
    private const string SteeredCode = "HostTwoFactorSetupRequired";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerTwoFactorPolicyEndpointTests(PostgresFixture postgres)
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
    /// With no policy stored the defaults apply, and they force a reseller: the sign-in says so, the token
    /// carries the enrolment marker, and the reseller's own screens answer with the enrolment code.
    /// </summary>
    [Fact]
    public async Task A_reseller_is_steered_to_enrolment_when_no_policy_is_stored()
    {
        await using var factory = await IdentityResellerWorld.BootAsync(_pg, forceTwoFactorForResellers: null);
        await IdentityResellerWorld.SeedResellerAsync(factory, "nova");

        var (signedIn, token) = await SignInAsync(factory, "nova");

        Assert.True(signedIn.RequiresTwoFactorSetup);
        Assert.True(new JsonWebTokenHandler().ReadJsonWebToken(token).TryGetClaim(PanelClaimTypes.TwoFactorSetupRequired, out _));
        using var client = IdentityResellerWorld.ClientWith(factory, token);
        foreach (var path in new[] { "/api/v1/reseller/me", "/api/v1/accounts", "/api/v1/plans" })
        {
            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(SteeredCode, await CodeOfAsync(response));
        }
    }

    /// <summary>
    /// Saving the reseller switch off through the administrator's endpoint is honoured at the very next
    /// sign-in: the reseller who was steered a moment before reaches its own screens.
    /// </summary>
    [Fact]
    public async Task Turning_the_policy_off_is_honoured_at_the_next_sign_in()
    {
        await using var factory = await IdentityResellerWorld.BootAsync(_pg, forceTwoFactorForResellers: null);
        await IdentityResellerWorld.SeedResellerAsync(factory, "nova");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var (before, _) = await SignInAsync(factory, "nova");
        var saved = await admin.PutAsJsonAsync("/api/v1/security-policy", new
        {
            minimumPasswordLength = 12,
            forceTwoFactorForAdmins = false,
            forceTwoFactorForResellers = false,
            maxFailedLoginAttempts = 10,
            lockoutMinutes = 15,
        });
        var (after, token) = await SignInAsync(factory, "nova");

        // The inverse control: the first sign-in was steered, so the second one being free is the switch.
        Assert.True(before.RequiresTwoFactorSetup);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.False(after.RequiresTwoFactorSetup);
        using var client = IdentityResellerWorld.ClientWith(factory, token);
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/v1/reseller/me"));
        Assert.Equal("nova", me.RootElement.GetProperty("name").GetString());
    }

    /// <summary>Forcing administrators and not resellers steers the administrator and leaves the reseller free.</summary>
    [Fact]
    public async Task The_administrator_switch_does_not_steer_a_reseller()
    {
        await using var factory = await IdentityResellerWorld.BootAsync(_pg, forceTwoFactorForResellers: null);
        await IdentityResellerWorld.SavePolicyAsync(factory, forceTwoFactorForAdmins: true, forceTwoFactorForResellers: false);
        await IdentityResellerWorld.SeedResellerAsync(factory, "nova");

        var (administrator, _) = await SignInAsync(factory, "admin");
        var (reseller, _) = await SignInAsync(factory, "nova");

        Assert.True(administrator.RequiresTwoFactorSetup);
        Assert.False(reseller.RequiresTwoFactorSetup);
    }

    /// <summary>Forcing resellers and not administrators steers the reseller and leaves the administrator free.</summary>
    [Fact]
    public async Task The_reseller_switch_does_not_steer_an_administrator()
    {
        await using var factory = await IdentityResellerWorld.BootAsync(_pg, forceTwoFactorForResellers: null);
        await IdentityResellerWorld.SavePolicyAsync(factory, forceTwoFactorForAdmins: false, forceTwoFactorForResellers: true);
        await IdentityResellerWorld.SeedResellerAsync(factory, "nova");

        var (administrator, _) = await SignInAsync(factory, "admin");
        var (reseller, _) = await SignInAsync(factory, "nova");

        Assert.False(administrator.RequiresTwoFactorSetup);
        Assert.True(reseller.RequiresTwoFactorSetup);
    }

    /// <summary>What a sign-in answered: whether the login must still enrol.</summary>
    /// <param name="RequiresTwoFactorSetup">Whether the session is steered to enrolment.</param>
    private sealed record SignedIn(bool RequiresTwoFactorSetup);

    /// <summary>Signs a login in and reads the steering flag and the access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login name.</param>
    /// <returns>What the answer said, and the access token.</returns>
    private static async Task<(SignedIn Answer, string Token)> SignInAsync(
        WebApplicationFactory<Program> factory,
        string username)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = username, Password = EndpointHarness.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var session = body.RootElement.GetProperty("session");

        return (
            new SignedIn(session.GetProperty("requiresTwoFactorSetup").GetBoolean()),
            session.GetProperty("accessToken").GetString()!);
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
