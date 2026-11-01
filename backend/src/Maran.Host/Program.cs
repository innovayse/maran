using Maran.Agent.Client;
using Maran.Host.Cli;
using Maran.Host.Cli.Extensions;
using Maran.Host.Cli.Models;
using Maran.Host.Configuration;
using Maran.Host.Dashboard;
using Maran.Host.Extensions;
using Maran.Host.HealthChecks;
using Maran.Host.Modules;
using Maran.Sdk;
using Maran.SharedKernel;

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

    /// <summary>Builds and runs the web host, or runs one command-line verb and exits.</summary>
    /// <param name="args">Command-line arguments passed through to <see cref="WebApplication"/>.</param>
    /// <returns>The process exit status: zero for a served host, the verb's own status for a CLI run.</returns>
    public static int Main(string[] args)
    {
        // Nothing the host starts — a timer, a pool's pruning loop, a hosted service — inherits the
        // execution context of whoever invoked Main. A real process has nothing in it worth inheriting.
        // A test host does: WebApplicationFactory runs Main on a thread whose context carries the
        // listener that holds the built host, so every timer created during startup that outlived the
        // host kept it alive — Npgsql's pool-pruning timers in a static registry, and the rate
        // limiter's (see UsePanelRateLimiting). Measured (issue #124): the integration suite kept one
        // whole host per finished test and was killed at a 6 GB cap five minutes in; with this line,
        // UsePanelRateLimiting and the test fixture's reclaim it completes at a 2.7 GB peak.
        using var detachedFromCaller = ExecutionContext.SuppressFlow();

        var cli = CliInvocationParser.Parse(args);
        var builder = WebApplication.CreateBuilder(args);
        var connectionString = ResolveConnectionString(builder.Configuration);
        PublishConnectionString(builder.Configuration, connectionString);

        builder.Host.AddPanelObservability(cli is not null);
        builder.Host.AddPanelMessaging(connectionString);

        builder.Services.AddPanelForwardedHeaders();
        builder.Services.AddPanelConfiguration(builder.Configuration);
        builder.Services.AddPanelLocalization();
        builder.Services.AddSharedKernel();
        builder.Services.AddSdk();
        builder.Services.AddPanelSecurity();
        builder.Services.AddPanelAuthentication(builder.Configuration);
        builder.Services.AddAgentClient(ResolveAgentSocketPath(builder.Configuration));
        builder.Services.AddPanelHealthChecks(connectionString);
        builder.Services.AddPanelResilience();
        builder.Services.AddPanelRateLimiting();
        builder.Services.AddPanelJsonSerialization();
        builder.Services.AddPanelModelBinding();
        builder.Services.AddPanelModules(builder.Configuration);
        builder.Services.AddPanelBackgroundWork();
        builder.Services.AddPanelSeeding();
        builder.Services.AddLicensingStartupCheck(builder.Configuration);
        builder.Services.AddPanelCli();

        var app = builder.Build();

        // The command-line mode (`--cli <verb>`, and `--migrate` as its alias): runs one verb against
        // the composed container and exits without serving anything. `--migrate` is the ONE place
        // this product applies schema changes — see Cli/Commands/MigrateCliCommand.cs for why.
        if (cli is not null)
        {
            return RunCli(app.Services, cli);
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
        app.UsePanelRateLimiting();

        app.MapPanelHealth();
        app.MapModuleCatalogue();
        app.MapDashboard();
        app.MapControllers();

        app.Run();
        return 0;
    }

    /// <summary>Runs one command-line verb to completion on the process's own streams.</summary>
    /// <param name="services">The built application's service provider.</param>
    /// <param name="invocation">The parsed command line and the process facts.</param>
    /// <returns>The verb's exit status.</returns>
    private static int RunCli(IServiceProvider services, CliInvocation invocation)
    {
        return services.GetRequiredService<CliRunner>()
            .RunAsync(invocation, new CliConsole(Console.In, Console.Out, Console.Error), CancellationToken.None)
            .GetAwaiter().GetResult();
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
