using Maran.Agent.Client;
using Maran.Host.Configuration;
using Maran.Host.Dashboard;
using Maran.Host.Extensions;
using Maran.Host.HealthChecks;
using Maran.Host.Modules;
using Maran.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host;

/// <summary>
/// Composition root of maran-api. Reads as a table of contents: every entry is one
/// <c>Add…</c>/<c>Use…</c>/<c>Map…</c> call whose implementation lives in its own file under
/// <c>Extensions/</c> (rules/csharp.md). Logic never belongs here.
/// </summary>
public sealed class Program
{
    /// <summary>
    /// Name of the connection string modules resolve with <c>GetConnectionString</c>. Shared with
    /// <c>AccountsModule</c> by convention rather than by reference: the Sdk deliberately does not
    /// depend on the host.
    /// </summary>
    private const string PanelConnectionStringName = "Panel";

    /// <summary>Builds and runs the web host.</summary>
    /// <param name="args">Command-line arguments passed through to <see cref="WebApplication"/>.</param>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var connectionString = ResolveConnectionString(builder.Configuration);
        PublishConnectionString(builder.Configuration, connectionString);

        builder.Host.AddPanelObservability();
        builder.Host.AddPanelMessaging(connectionString);

        builder.Services.AddPanelForwardedHeaders();
        builder.Services.AddPanelConfiguration(builder.Configuration);
        builder.Services.AddPanelLocalization();
        builder.Services.AddSharedKernel();
        builder.Services.AddPanelSecurity();
        builder.Services.AddPanelAuthentication(builder.Configuration);
        builder.Services.AddAgentClient(ResolveAgentSocketPath(builder.Configuration));
        builder.Services.AddPanelHealthChecks(connectionString);
        builder.Services.AddPanelResilience();
        builder.Services.AddPanelRateLimiting();
        builder.Services.AddPanelJsonSerialization();
        builder.Services.AddPanelModules(builder.Configuration);
        builder.Services.AddPanelBackgroundWork();
        builder.Services.AddPanelSeeding();
        builder.Services.AddLicensingStartupCheck(builder.Configuration);

        var app = builder.Build();

        // The migration mode, and the ONE place this product applies schema changes.
        //
        // rules/architecture.md and scripts/lib/migrations.sh both say migrations are never applied
        // by a starting process — "the installer and the update command apply them deliberately".
        // The installer had no way to do that: a server carries the self-contained panel and no .NET
        // SDK, so `dotnet ef` is not available to it. Nothing applied them, and every fresh install
        // came up with no module schema at all — the panel served its setup page and answered
        // `relation "identity.Users" does not exist` to the first thing an operator does (issue #66).
        //
        // This keeps the rule and closes the gap: the panel applies migrations only when ASKED to,
        // by the installer, and exits without serving anything. A normal start still migrates
        // nothing.
        if (args.Contains("--migrate", StringComparer.Ordinal))
        {
            MigrateAsync(app.Services).GetAwaiter().GetResult();
            return;
        }

        // Before the pipeline serves anything: the panel's listening socket is its trust boundary,
        // and Kestrel creates it world-connectable.
        app.UsePanelListenSocketGuard();

        // First two in the pipeline, before anything reads an address: the rate limiter partitions
        // on it, the audit journal records it, and both must see the caller rather than nginx.
        // The order of this pair is load-bearing — UsePanelPeerAddress feeds UseForwardedHeaders
        // the peer address it compares against KnownProxies, and over a unix socket there is no
        // such address until it does.
        app.UsePanelPeerAddress();
        app.UseForwardedHeaders();
        app.UseSecurityHeaders();
        app.UseCorrelationId();
        app.UsePanelRequestLogging();
        // Localisation MUST precede exception handling. Both orders look right from the request
        // side — the culture is set before any controller runs either way — but CurrentUICulture is
        // an async-local, and a value assigned in a nested flow does not propagate back out to the
        // frame that catches. With the handler installed first, it resolved every error text in the
        // parent context, where no culture had been set, and ResourceManager fell back to the
        // neutral English resx. Silently: a fallback is what a resource manager is for. Every
        // translation this product ships was unreachable, in every module, and `maran structure`
        // went on requiring all three locales to carry every key. Held by
        // Maran.Host.IntegrationTests/ErrorMessageLocalizationTests.cs, which asks two modules in
        // three languages, because the mechanism is this pipeline and not any module's resources.
        app.UsePanelLocalization();
        app.UseExceptionHandling();
        app.UseCsrfHeader();
        app.UsePanelAuthentication();
        app.UseRateLimiter();

        app.MapPanelHealth();
        app.MapModuleCatalogue();
        app.MapDashboard();
        app.MapControllers();

        app.Run();
    }

    /// <summary>
    /// Applies every registered module's pending migrations, in one pass, and reports each by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The contexts are DISCOVERED from the container rather than listed here, and that is the whole
    /// point: twelve modules register a <see cref="DbContext"/> today, and a hardcoded list is a
    /// place to forget the thirteenth. A module that registers a context is migrated by existing.
    /// </para>
    /// <para>
    /// Each context is resolved in its own scope because a <see cref="DbContext"/> is scoped, and
    /// migrated through the EF API rather than through generated SQL so that the migration history
    /// table stays the authority on what has been applied.
    /// </para>
    /// <para>
    /// A failure is not swallowed: it propagates, the process exits non-zero, and the installer stops
    /// on it. An install that could not create the schema must fail where the operator can see it,
    /// not sixty seconds later as an HTTP 500 with a correlation id.
    /// </para>
    /// </remarks>
    /// <param name="services">The built application's service provider.</param>
    /// <returns>Resolves when every registered context has been migrated.</returns>
    private static async Task MigrateAsync(IServiceProvider services)
    {
        var contextTypes = services.GetServices<DbContextOptions>()
            .Select(options => { return options.ContextType; })
            .Distinct()
            .OrderBy(type => { return type.Name; }, StringComparer.Ordinal)
            .ToList();

        // A vacuity guard, because "nothing to migrate" and "nothing was found to migrate" look
        // identical in a log and only one of them is good news (rules/testing.md).
        if (contextTypes.Count == 0)
        {
            throw new InvalidOperationException(
                "--migrate found no registered DbContext at all, so it would report success having " +
                "migrated nothing. Refusing rather than leaving an empty schema behind.");
        }

        foreach (var contextType in contextTypes)
        {
            await using var scope = services.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            var pending = (await context.Database.GetPendingMigrationsAsync().ConfigureAwait(false)).ToList();
            if (pending.Count == 0)
            {
                Console.WriteLine($"{contextType.Name}: up to date.");
                continue;
            }

            await context.Database.MigrateAsync().ConfigureAwait(false);
            Console.WriteLine($"{contextType.Name}: applied {pending.Count} migration(s).");
        }
    }

    /// <summary>
    /// Builds the database connection string from the individual <c>Database:*</c> settings, before
    /// the options system is available to resolve. Separate keys are the contract (rules/security.md);
    /// the assembled string exists only inside this process.
    /// </summary>
    /// <param name="configuration">The builder's configuration, holding the <c>Database</c> section.</param>
    /// <returns>The connection string, or an empty string when no database is configured.</returns>
    private static string ResolveConnectionString(ConfigurationManager configuration)
    {
        return (configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions())
            .BuildConnectionString();
    }

    /// <summary>
    /// Publishes the assembled connection string as <c>ConnectionStrings:Panel</c> so modules can
    /// read it the ordinary ASP.NET Core way.
    /// </summary>
    /// <remarks>
    /// The panel configures its database as separate <c>Database:*</c> settings — a password never
    /// belongs in a semicolon-joined blob an operator might paste into a ticket — and the string is
    /// assembled here, once. Modules, however, are given only <see cref="IConfiguration"/>, and a
    /// module that reassembled the string itself would drift from the host the first time a setting
    /// was added. Without this line the Accounts module read a key nobody wrote and opened every
    /// connection against an empty server name.
    /// </remarks>
    /// <param name="configuration">The configuration to publish onto.</param>
    /// <param name="connectionString">The assembled connection string.</param>
    private static void PublishConnectionString(ConfigurationManager configuration, string connectionString)
    {
        configuration[$"ConnectionStrings:{PanelConnectionStringName}"] = connectionString;
    }

    /// <summary>
    /// Reads the agent socket path for the client registration, which happens before the options
    /// system can be resolved from the container.
    /// </summary>
    /// <param name="configuration">The builder's configuration, holding the <c>Agent</c> section.</param>
    /// <returns>The configured socket path, or the production default.</returns>
    private static string ResolveAgentSocketPath(ConfigurationManager configuration)
    {
        return configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>()?.SocketPath
        ?? new AgentOptions().SocketPath;
    }
}
