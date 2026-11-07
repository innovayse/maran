using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Where the base of an invitation link comes from, over real HTTP: the <c>Panel:PublicUrl</c> setting
/// and nothing a caller sends (<c>docs/superpowers/notes/2026-11-07-panel-public-url-threat-note.md</c>).
/// Each claim has its inverse control: the configured base IS used, a hostile <c>Host</c> header does
/// not move it, and a malformed setting stops the boot while a well-formed one does not.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class InvitationLinkBaseEndpointTests : IAsyncLifetime
{
    /// <summary>The origin the tests configure as the panel's public address.</summary>
    private const string PublicUrl = "https://203.0.113.7:8443";

    /// <summary>A key of the right shape; startup validation refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public InvitationLinkBaseEndpointTests(PostgresFixture postgres)
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

    /// <summary>The link an administrator is given starts with the configured public address.</summary>
    [Fact]
    public async Task The_invitation_link_starts_with_the_configured_public_address()
    {
        await using var factory = CreateFactory(PublicUrl);
        var (admin, accountId) = await ArrangeAsync(factory);
        using var _ = admin;

        var link = await IssueLinkAsync(admin, accountId, hostile: false);

        Assert.StartsWith($"{PublicUrl}/accept-invitation?token=", link, StringComparison.Ordinal);
    }

    /// <summary>A hostile Host or X-Forwarded-Host header does not move the base of the link.</summary>
    [Fact]
    public async Task A_hostile_host_header_does_not_change_the_base_of_the_link()
    {
        await using var factory = CreateFactory(PublicUrl);
        var (admin, accountId) = await ArrangeAsync(factory);
        using var _ = admin;

        var link = await IssueLinkAsync(admin, accountId, hostile: true);

        Assert.StartsWith($"{PublicUrl}/accept-invitation?token=", link, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", link, StringComparison.Ordinal);
    }

    /// <summary>Without a configured address the link is a root-relative path, as it always was.</summary>
    [Fact]
    public async Task Without_a_configured_address_the_link_is_a_root_relative_path()
    {
        await using var factory = CreateFactory(publicUrl: null);
        var (admin, accountId) = await ArrangeAsync(factory);
        using var _ = admin;

        var link = await IssueLinkAsync(admin, accountId, hostile: true);

        Assert.StartsWith("/accept-invitation?token=", link, StringComparison.Ordinal);
    }

    /// <summary>A malformed public address stops the boot and names the setting, not its value.</summary>
    [Theory]
    [InlineData("http://panel.example.com")]
    [InlineData("https://user@panel.example.com")]
    [InlineData("https://panel.example.com/path")]
    [InlineData("https://panel.example.com:99999")]
    public async Task A_malformed_public_address_stops_the_boot(string malformed)
    {
        await using var factory = CreateFactory(malformed);

        var failure = Assert.ThrowsAny<Exception>(() =>
        {
            using var client = factory.CreateClient();
        });

        Assert.Contains("Panel:PublicUrl", failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(malformed, failure.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Boots the host with <paramref name="publicUrl"/> as the panel's public address.</summary>
    /// <param name="publicUrl">The address to configure, or null to configure none.</param>
    /// <returns>The factory, which the caller disposes.</returns>
    private WebApplicationFactory<Program> CreateFactory(string? publicUrl)
    {
        return new ConfiguredHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);
            if (publicUrl is not null)
            {
                builder.UseSetting("Panel:PublicUrl", publicUrl);
            }

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    /// <summary>Migrates, seeds an administrator and an invited account owner, and signs the administrator in.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The administrator's client and the account to invite.</returns>
    private static async Task<(HttpClient Admin, Guid AccountId)> ArrangeAsync(WebApplicationFactory<Program> factory)
    {
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedAdministratorAsync(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
            var invited = User.Invite(Guid.NewGuid(), "owner-olive", "owner-olive@example.com", now);
            context.Users.Add(invited);
            context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), invited.Id, accountId, now));
            context.InvitationTokens.Add(
                new InvitationToken(Guid.NewGuid(), invited.Id, InvitationTokenHasher.Hash("a-token"), now));
            await context.SaveChangesAsync();
        }

        return (await EndpointHarness.SignInAsync(factory, "admin"), accountId);
    }

    /// <summary>Asks for an invitation link and returns it.</summary>
    /// <param name="admin">A client signed in as the administrator.</param>
    /// <param name="accountId">The account to invite.</param>
    /// <param name="hostile">Whether the request claims to be addressed to a host the attacker owns.</param>
    /// <returns>The link from the response.</returns>
    private static async Task<string> IssueLinkAsync(HttpClient admin, Guid accountId, bool hostile)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/invitations/{accountId}/link");
        if (hostile)
        {
            request.Headers.Host = "evil.example";
            request.Headers.Add("X-Forwarded-Host", "evil.example");
        }

        var response = await admin.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("link").GetString()!;
    }
}
