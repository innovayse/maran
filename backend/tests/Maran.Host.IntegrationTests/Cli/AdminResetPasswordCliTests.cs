using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Maran.Host.Cli;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Cli;

/// <summary>
/// <c>Maran.Host --cli admin-reset-password</c> against a real database and the real HTTP surface: an
/// administrator who is locked out is recovered by the verb, signs in with the password they choose on
/// the link it printed, and nothing about the old state survives.
/// </summary>
/// <remarks>
/// The verb is driven through the composed container's <see cref="CliRunner"/>, which is exactly what
/// <c>Program</c> does after building the host, with the process facts (terminal, root, sudo user) stated
/// by the test. The reset itself is the shipped endpoint, so the link is proven to work and not merely
/// to look like a link.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AdminResetPasswordCliTests : IAsyncLifetime
{
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string OldPassword = "the old password is long";
    private const string NewPassword = "correct horse battery staple";
    private const string Verb = "admin-reset-password";

    private readonly TestDatabase _pg;

    /// <summary>The unconfigured factories each derived factory was built from, released with the test.</summary>
    private readonly List<WebApplicationFactory<Program>> _roots = new();

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AdminResetPasswordCliTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares a fresh database before each test.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases the root factories; the shared server owns the databases.</summary>
    public async Task DisposeAsync()
    {
        foreach (var root in _roots)
        {
            await root.DisposeAsync();
        }
    }

    /// <summary>Boots the real panel against the test database.</summary>
    /// <returns>The configured factory.</returns>
    private WebApplicationFactory<Program> CreateFactory()
    {
        var root = new WebApplicationFactory<Program>();
        _roots.Add(root);
        return root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);
            builder.UseSetting("PasswordReset:PanelUrl", "https://panel.example.com");
            builder.UseSetting("RateLimiting:PasswordResetMaxRequests", "50");

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    /// <summary>Creates the schema and one login of the given role.</summary>
    /// <param name="factory">The booted panel.</param>
    /// <param name="username">The login name.</param>
    /// <param name="role">The login's role.</param>
    /// <returns>The new user's id.</returns>
    private static async Task<Guid> SeedUserAsync(
        WebApplicationFactory<Program> factory,
        string username,
        UserRole role)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identity.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var user = new User(
            Guid.NewGuid(), username, $"{username}@example.com", hasher.Hash(OldPassword), role, clock.UtcNow);
        identity.Users.Add(user);
        await identity.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>Locks a login out the way repeated wrong passwords do.</summary>
    /// <param name="factory">The booted panel.</param>
    /// <param name="userId">The login to lock.</param>
    private static async Task LockOutAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var user = await identity.Users.SingleAsync(candidate => candidate.Id == userId);
        user.RecordFailedLogin(clock.UtcNow, 1, TimeSpan.FromMinutes(15));
        await identity.SaveChangesAsync();
        Assert.True(user.IsLockedOut(clock.UtcNow), "The setup did not lock the login, so the test proves nothing.");
    }

    /// <summary>Runs the verb through the composed container, as <c>Program</c> does.</summary>
    /// <param name="factory">The booted panel.</param>
    /// <param name="terminal">Whether standard output counts as a terminal.</param>
    /// <param name="arguments">The verb's arguments.</param>
    /// <returns>The exit status and what the verb wrote to each stream.</returns>
    private static async Task<(int Status, string Output, string Error)> RunVerbAsync(
        WebApplicationFactory<Program> factory,
        bool terminal,
        params string[] arguments)
    {
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = factory.Services.GetRequiredService<CliRunner>();

        var status = await runner.RunAsync(
            new CliInvocation(Verb, arguments, terminal, false, "alice"),
            new CliConsole(input, output, error),
            CancellationToken.None);

        return (status, output.ToString(), error.ToString());
    }

    /// <summary>Extracts the reset token from the link the verb printed.</summary>
    /// <param name="link">The verb's standard output.</param>
    /// <returns>The token.</returns>
    private static string TokenFrom(string link)
    {
        var match = Regex.Match(link, @"token=([A-Za-z0-9_\-]+)", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(match.Success, "The verb printed no reset link.");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    /// <summary>
    /// A locked-out administrator is recovered end to end: the lockout is cleared, old sessions are dead,
    /// the link resets the password exactly once, an audit row names the recovery, and only the link
    /// reaches standard output.
    /// </summary>
    [Fact]
    public async Task A_locked_out_administrator_is_recovered_by_the_verb_and_the_link_works_exactly_once()
    {
        await using var factory = CreateFactory();
        var userId = await SeedUserAsync(factory, "admin", UserRole.Admin);
        using var client = factory.CreateClient();

        var firstSignIn = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "admin", Password = OldPassword });
        Assert.Equal(HttpStatusCode.OK, firstSignIn.StatusCode);
        await LockOutAsync(factory, userId);

        var lockedOut = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "admin", Password = OldPassword });
        Assert.NotEqual(HttpStatusCode.OK, lockedOut.StatusCode);

        var run = await RunVerbAsync(factory, true, "admin");

        Assert.Equal((int)CliExitCode.Success, run.Status);
        var token = TokenFrom(run.Output);
        Assert.DoesNotContain(token, run.Error, StringComparison.Ordinal);

        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var user = await identity.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == userId);

            Assert.False(user.IsLockedOut(clock.UtcNow));
            Assert.Equal(0, user.FailedLoginAttempts);

            // The session of the first sign-in is dead before the new password is even chosen.
            var live = await identity.Sessions.CountAsync(
                session => session.UserId == userId && session.Revocation == null);
            Assert.Equal(0, live);

            var audit = await identity.AuditEvents
                .AsNoTracking()
                .Where(entry => entry.Action == AuditActions.ConsoleRecovery)
                .ToListAsync();
            Assert.Single(audit);
            Assert.DoesNotContain(token, audit[0].Subject, StringComparison.Ordinal);
        }

        var reset = await client.PostAsJsonAsync(
            "/api/v1/auth/reset-password", new { Token = token, NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var withNew = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "admin", Password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);

        var replay = await client.PostAsJsonAsync(
            "/api/v1/auth/reset-password", new { Token = token, NewPassword = "another password entirely" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    /// <summary>A pipe is refused without the flag, and nothing was minted or changed.</summary>
    [Fact]
    public async Task Output_that_is_not_a_terminal_is_refused_and_no_token_is_minted()
    {
        await using var factory = CreateFactory();
        await SeedUserAsync(factory, "admin", UserRole.Admin);

        var run = await RunVerbAsync(factory, false, "admin");

        Assert.Equal((int)CliExitCode.Refused, run.Status);
        Assert.Equal(string.Empty, run.Output);
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(0, await identity.PasswordResetTokens.CountAsync());
    }

    /// <summary>With the flag the same pipe gets the link (the inverse control for the refusal above).</summary>
    [Fact]
    public async Task The_allow_non_terminal_flag_lets_a_pipe_receive_the_link()
    {
        await using var factory = CreateFactory();
        await SeedUserAsync(factory, "admin", UserRole.Admin);

        var run = await RunVerbAsync(factory, false, "admin", "--allow-non-terminal");

        Assert.Equal((int)CliExitCode.Success, run.Status);
        Assert.NotEmpty(TokenFrom(run.Output));
    }

    /// <summary>A customer login is refused: this path recovers administrators only.</summary>
    [Fact]
    public async Task A_customer_login_is_refused_and_no_token_is_minted()
    {
        await using var factory = CreateFactory();
        await SeedUserAsync(factory, "customer", UserRole.Customer);

        var run = await RunVerbAsync(factory, true, "customer");

        Assert.Equal((int)CliExitCode.Refused, run.Status);
        Assert.Equal(string.Empty, run.Output);
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(0, await identity.PasswordResetTokens.CountAsync());
    }

    /// <summary>An address nobody has is refused, and says nothing on standard output.</summary>
    [Fact]
    public async Task An_unknown_login_is_refused()
    {
        await using var factory = CreateFactory();
        await SeedUserAsync(factory, "admin", UserRole.Admin);

        var run = await RunVerbAsync(factory, true, "nobody@example.com");

        Assert.Equal((int)CliExitCode.Refused, run.Status);
        Assert.Equal(string.Empty, run.Output);
    }
}
