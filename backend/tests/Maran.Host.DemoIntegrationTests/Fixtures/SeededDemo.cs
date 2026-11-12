using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Maran.Host.DemoIntegrationTests.Fixtures;

/// <summary>
/// A demo host booted against its own database, migrated and seeded by the same CLI verbs the operator runs,
/// with the published sign-ins read from the status endpoint exactly as a visitor reads them.
/// </summary>
public sealed class SeededDemo : IAsyncDisposable
{
    /// <summary>A key of the right shape; startup validation refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The abuse contact every test host is configured with; the status endpoint must return it.</summary>
    public const string AbuseContact = "abuse@demo.example";

    /// <summary>The address a request appears to come from when a test names none.</summary>
    private static readonly IPAddress DefaultPeer = IPAddress.Parse("203.0.113.1");

    /// <summary>The header the CSRF check wants on a mutating call that carries a cookie.</summary>
    private const string RequestHeader = "X-Maran-Request";

    /// <summary>The booted host.</summary>
    public DemoHostFactory Factory { get; }

    /// <summary>The connection string of the host's database.</summary>
    public string ConnectionString { get; }

    /// <summary>The published administrator sign-in.</summary>
    public (string Username, string Password) Administrator { get; }

    /// <summary>Wraps a booted, seeded host.</summary>
    /// <param name="factory">The host.</param>
    /// <param name="connectionString">Its database.</param>
    /// <param name="administrator">The published administrator sign-in.</param>
    private SeededDemo(DemoHostFactory factory, string connectionString, (string, string) administrator)
    {
        Factory = factory;
        ConnectionString = connectionString;
        Administrator = administrator;
    }

    /// <summary>Boots, migrates and seeds a demo host.</summary>
    /// <param name="postgres">The shared server.</param>
    /// <param name="settings">Extra configuration, such as small budgets.</param>
    /// <param name="peer">The address every request appears to come from, or null for none.</param>
    /// <returns>The seeded host.</returns>
    public static async Task<SeededDemo> StartAsync(
        DemoPostgres postgres,
        IReadOnlyDictionary<string, string>? settings = null,
        IPAddress? peer = null)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);

        // The host that migrates and seeds boots against a database that holds no demonstration yet, and the
        // served host refuses to start in that state (its start-up step throws on an empty protected set),
        // so the first boot leaves that step out; the host that serves is booted after the seed, in full.
        var seeding = CreateFactory(parsed, settings, peer, leaveOutProtectionStartup: true);
        try
        {
            await RunVerbAsync(seeding, "migrate");
            await RunVerbAsync(seeding, "demo-seed");
        }
        finally
        {
            await seeding.DisposeAsync();
        }

        var factory = CreateFactory(parsed, settings, peer, leaveOutProtectionStartup: false);

        using var client = factory.CreateClient();
        using var status = JsonDocument.Parse(await client.GetStringAsync("/api/v1/demo/status"));
        var administrator = status.RootElement.GetProperty("identities").EnumerateArray()
            .Single(identity => { return identity.GetProperty("role").GetString() == "administrator"; });

        return new SeededDemo(
            factory,
            connectionString,
            (administrator.GetProperty("username").GetString()!, administrator.GetProperty("password").GetString()!));
    }

    /// <summary>
    /// Migrates a database and returns a host over it that has NOT been booted and holds no demonstration, to
    /// prove what a served host does when it finds nothing to protect.
    /// </summary>
    /// <param name="postgres">The shared server.</param>
    /// <returns>The host factory; the caller disposes it.</returns>
    public static async Task<DemoHostFactory> StartMigratedWithoutSeedAsync(DemoPostgres postgres)
    {
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(await postgres.CreateDatabaseAsync());

        var migrating = CreateFactory(parsed, null, null, leaveOutProtectionStartup: true);
        try
        {
            await RunVerbAsync(migrating, "migrate");
        }
        finally
        {
            await migrating.DisposeAsync();
        }

        return CreateFactory(parsed, null, null, leaveOutProtectionStartup: false);
    }

    /// <summary>
    /// Signs the published administrator in and returns a client carrying the access token and cookie.
    /// </summary>
    /// <param name="peer">The address this visitor appears to come from, or null for the host's default.</param>
    /// <returns>A client the caller disposes.</returns>
    public async Task<HttpClient> SignInAsync(string? peer = null)
    {
        var (client, _) = await SignInKeepingRefreshCookieAsync(peer);
        return client;
    }

    /// <summary>
    /// Signs the published administrator in, and also hands back the refresh cookie the answer set, so a test
    /// can present it again after the client's own copy has been cleared by a sign-out.
    /// </summary>
    /// <param name="peer">The address this visitor appears to come from, or null for the host's default.</param>
    /// <returns>A client the caller disposes, and the refresh cookie as <c>name=value</c>.</returns>
    public async Task<(HttpClient Client, string RefreshCookie)> SignInKeepingRefreshCookieAsync(string? peer = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(RequestHeader, "1");
        if (peer is not null)
        {
            client.DefaultRequestHeaders.Add(FixedPeerStartupFilter.HeaderName, peer);
        }

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Username = Administrator.Username, Password = Administrator.Password });
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var refreshCookie = response.Headers.GetValues("Set-Cookie")
            .Select(header => { return header.Split(';')[0]; })
            .Single(pair => { return pair.StartsWith(Maran.Modules.Identity.Controllers.RefreshCookie.Name + "=", StringComparison.Ordinal); });
        return (client, refreshCookie);
    }

    /// <summary>Stops the host.</summary>
    /// <returns>Resolves once it is disposed.</returns>
    public ValueTask DisposeAsync()
    {
        return Factory.DisposeAsync();
    }

    /// <summary>Builds a demo host over a database.</summary>
    /// <param name="parsed">The database the host connects to.</param>
    /// <param name="settings">Extra configuration, such as small budgets.</param>
    /// <param name="peer">The address every request appears to come from, or null for none.</param>
    /// <param name="leaveOutProtectionStartup">
    /// Whether to remove the step that fills the protected set at start, for a host that boots before the seed.
    /// </param>
    /// <returns>The host factory; nothing is booted until it is used.</returns>
    private static DemoHostFactory CreateFactory(
        Npgsql.NpgsqlConnectionStringBuilder parsed,
        IReadOnlyDictionary<string, string>? settings,
        IPAddress? peer,
        bool leaveOutProtectionStartup)
    {
        return new DemoHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:Host", parsed.Host ?? "localhost");
            builder.UseSetting(
                "Database:Port",
                parsed.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Database:Database", parsed.Database ?? string.Empty);
            builder.UseSetting("Database:Username", parsed.Username ?? string.Empty);
            builder.UseSetting("Database:Password", parsed.Password ?? string.Empty);
            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            // The first-run token the seed's administrator step redeems; the real environment file carries one.
            builder.UseSetting("Setup:Token", "a-one-time-token");

            // The shipped file names the demonstration's own domain, which the test client's host name is not.
            builder.UseSetting("AllowedHosts", "localhost");
            builder.UseSetting("Firewall:SshPorts", "22");
            builder.UseSetting("Firewall:PanelPort", "8443");

            // The two published sign-ins come from the deployment's environment file, never from source.
            builder.UseSetting("Demo:Identities:AdministratorPassword", "test-admin-password-1");
            builder.UseSetting("Demo:Identities:CustomerPassword", "test-customer-password-1");

            // The abuse contact is required in demo mode; the host refuses to start without one.
            builder.UseSetting("Demo:AbuseContact", AbuseContact);

            // The host that seeds creates the world through the real commands, so its simulated server starts
            // empty, as the one `demo-seed` runs against does; the host that serves is booted with the world.
            if (leaveOutProtectionStartup)
            {
                builder.UseSetting(Maran.Host.Demo.Extensions.DemoWorldExtensions.SimulatedWorldSetting, "false");
            }

            foreach (var setting in settings ?? new Dictionary<string, string>())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureServices(services =>
            {
                // Always installed: the in-memory server reports no address, and without one every visitor is the
                // same visitor, so a test that sends the header to be someone else would not be.
                services.AddSingleton<IStartupFilter>(new FixedPeerStartupFilter(peer ?? DefaultPeer));

                if (leaveOutProtectionStartup)
                {
                    var startup = services.Single(descriptor =>
                    {
                        return descriptor.ServiceType == typeof(IHostedService)
                            && descriptor.ImplementationType
                                == typeof(Maran.Host.Demo.Seeding.Services.DemoProtectedResourceStartup);
                    });
                    services.Remove(startup);
                }
            });
        });
    }

    /// <summary>Runs one operator verb against the booted host and requires it to succeed.</summary>
    /// <param name="factory">The host.</param>
    /// <param name="verb">The verb.</param>
    private static async Task RunVerbAsync(DemoHostFactory factory, string verb)
    {
        using var scope = factory.Services.CreateScope();
        var command = scope.ServiceProvider.GetServices<ICliCommand>().Single(
            candidate => { return candidate.Verb == verb; });
        var output = new StringWriter();
        var result = await command.ExecuteAsync(
            new CliInvocation(verb, [], false, true, null),
            new CliConsole(TextReader.Null, output, output),
            CancellationToken.None);

        Assert.True(result.ExitCode == CliExitCode.Success, $"{verb} failed: {output}");
    }
}
