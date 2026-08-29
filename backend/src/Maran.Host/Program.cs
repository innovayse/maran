using Maran.Agent.Client;
using Maran.Host.Configuration;
using Maran.Host.Extensions;
using Maran.Host.HealthChecks;
using Maran.Host.Modules;
using Maran.SharedKernel;

namespace Maran.Host;

/// <summary>
/// Composition root of maran-api. Reads as a table of contents: every entry is one
/// <c>Add…</c>/<c>Use…</c>/<c>Map…</c> call whose implementation lives in its own file under
/// <c>Extensions/</c> (rules/csharp.md). Logic never belongs here.
/// </summary>
public sealed class Program
{
    /// <summary>Builds and runs the web host.</summary>
    /// <param name="args">Command-line arguments passed through to <see cref="WebApplication"/>.</param>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var connectionString = ResolveConnectionString(builder.Configuration);

        builder.Host.AddPanelObservability();
        builder.Host.AddPanelMessaging(connectionString);

        builder.Services.AddPanelConfiguration(builder.Configuration);
        builder.Services.AddPanelLocalization();
        builder.Services.AddSharedKernel();
        builder.Services.AddPanelSecurity();
        builder.Services.AddAgentClient(ResolveAgentSocketPath(builder.Configuration));
        builder.Services.AddPanelHealthChecks(connectionString);
        builder.Services.AddPanelResilience();
        builder.Services.AddPanelRateLimiting();
        builder.Services.AddPanelJsonSerialization();
        builder.Services.AddPanelModules(builder.Configuration);

        var app = builder.Build();

        app.UseCorrelationId();
        app.UsePanelRequestLogging();
        app.UseExceptionHandling();
        app.UsePanelLocalization();
        app.UseRateLimiter();

        app.MapPanelHealth();
        app.MapModuleCatalogue();
        app.MapControllers();
        app.MapPanelModules();

        app.Run();
    }

    /// <summary>
    /// Builds the database connection string from the individual <c>Database:*</c> settings, before
    /// the options system is available to resolve. Separate keys are the contract (rules/security.md);
    /// the assembled string exists only inside this process.
    /// </summary>
    /// <param name="configuration">The builder's configuration, holding the <c>Database</c> section.</param>
    /// <returns>The connection string, or an empty string when no database is configured.</returns>
    private static string ResolveConnectionString(ConfigurationManager configuration) =>
        (configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions())
            .BuildConnectionString();

    /// <summary>
    /// Reads the agent socket path for the client registration, which happens before the options
    /// system can be resolved from the container.
    /// </summary>
    /// <param name="configuration">The builder's configuration, holding the <c>Agent</c> section.</param>
    /// <returns>The configured socket path, or the production default.</returns>
    private static string ResolveAgentSocketPath(ConfigurationManager configuration) =>
        configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>()?.SocketPath
        ?? new AgentOptions().SocketPath;
}
