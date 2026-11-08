using Maran.Agent.Simulated;
using Maran.Host.Cli;
using Maran.Host.Cli.Extensions;
using Maran.Host.Cli.Models;
using Maran.Host.Configuration;
using Maran.Host.Dashboard;
using Maran.Host.Demo.Cli;
using Maran.Host.Demo.Extensions;
using Maran.Host.Extensions;
using Maran.Host.HealthChecks;
using Maran.Host.Modules;
using Maran.Host.Search;
using Maran.Sdk;
using Maran.SharedKernel;

namespace Maran.Host.Demo;

/// <summary>
/// Composition root of the public demonstration. It composes the same modules as <c>Maran.Host</c> and
/// differs in exactly three ways: a simulated agent stands where the agent client stood, the demo guard
/// stands between the visitor and the write surface, and the demonstration's world is seeded and
/// described. Reads as a table of contents like the Host's; logic never belongs here.
/// </summary>
public sealed class Program
{
    /// <summary>Name of the connection string modules resolve with <c>GetConnectionString</c>.</summary>
    private const string PanelConnectionStringName = "Panel";

    /// <summary>Builds and runs the web host, or runs one command-line verb and exits.</summary>
    /// <param name="args">Command-line arguments passed through to <see cref="WebApplication"/>.</param>
    /// <returns>The process exit status: zero for a served host, the verb's own status for a CLI run.</returns>
    public static int Main(string[] args)
    {
        // As in the Host: nothing the process starts inherits the invoker's execution context.
        using var detachedFromCaller = ExecutionContext.SuppressFlow();

        var cli = CliInvocationParser.Parse(args);
        var builder = WebApplication.CreateBuilder(args);
        var connectionString = (builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
            ?? new DatabaseOptions()).BuildConnectionString();
        builder.Configuration[$"ConnectionStrings:{PanelConnectionStringName}"] = connectionString;

        // The run that seeds creates the world through the real commands, so the simulated server it talks to
        // must not already hold it; only a host that serves visitors starts out with the world in place.
        if (cli?.Verb == DemoSeedCliCommand.VerbName)
        {
            builder.Configuration[DemoWorldExtensions.SimulatedWorldSetting] = "false";
        }

        builder.Host.AddPanelObservability(cli is not null);
        builder.Host.AddPanelMessaging(connectionString);

        builder.Services.AddPanelForwardedHeaders();
        builder.Services.AddPanelConfiguration(builder.Configuration);
        builder.Services.AddPanelLocalization();
        builder.Services.AddSharedKernel();
        builder.Services.AddSdk();
        builder.Services.AddPanelSecurity();
        builder.Services.AddPanelAuthentication(builder.Configuration);
        builder.Services.AddSimulatedAgent();
        builder.Services.AddPanelHealthChecks(connectionString);
        builder.Services.AddPanelResilience();
        builder.Services.AddPanelDashboardStream(builder.Configuration);
        builder.Services.AddPanelRateLimiting();
        builder.Services.AddPanelJsonSerialization();
        builder.Services.AddPanelModelBinding();
        builder.Services.AddPanelModules(builder.Configuration);
        builder.Services.AddPanelSearch();
        builder.Services.AddPanelBackgroundWork();
        builder.Services.AddPanelSeeding();
        builder.Services.AddLicensingStartupCheck(builder.Configuration);
        builder.Services.AddDemoGuard(builder.Configuration);
        builder.Services.AddPanelCli();
        builder.Services.AddDemoWorld(builder.Configuration);
        builder.Services.AddPanelOpenApi(cli);

        var app = builder.Build();

        if (cli is not null)
        {
            return app.Services.GetRequiredService<CliRunner>()
                .RunAsync(cli, new CliConsole(Console.In, Console.Out, Console.Error), CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        app.UsePanelListenSocketGuard();

        // The pair that comes first in the Host, then the demonstration's own: the visitor's address is
        // replaced by its stand-in before anything reads, logs or rate-limits on it.
        app.UsePanelPeerAddress();
        app.UseForwardedHeaders();
        app.UseClientPseudonym();
        app.UseSecurityHeaders();
        app.UseCorrelationId();
        app.UsePanelRequestLogging();
        app.UsePanelLocalization();
        app.UseExceptionHandling();
        app.UseCsrfHeader();
        app.UsePanelAuthentication();
        app.UseDemoWriteGuard();
        app.UsePanelRateLimiting();

        app.MapPanelHealth();
        app.MapModuleCatalogue();
        app.MapDashboard();
        app.MapDashboardStream();
        app.MapAttentionCount();
        app.MapFirstRun();
        app.MapSearch();
        app.MapControllers();

        app.Run();
        return 0;
    }
}
