using System.Resources;
using Maran.Modules.Tasks.Jobs;
using Maran.Modules.Tasks.Options;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Tasks;

/// <summary>
/// The Tasks module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="TasksDbContext"/> against the <c>tasks</c> PostgreSQL schema, contributes the module's
/// controllers to the Host's routing, and — the one thing no other module does — supplies the
/// panel-wide <see cref="ITaskRecorder"/> every other module records its long operations through
/// (spec §11).
/// </summary>
public sealed class TasksModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Tasks.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Tasks.Resources.DisplayNames";

    /// <inheritdoc />
    public string Name
    {
        get
        {
            return Manifest.Id;
        }
    }

    /// <inheritdoc />
    public Manifest Manifest
    {
        get
        {
            return TasksManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped by default, which is what the administrator query filter requires: the context
        // closes over the request's own ICurrentUser, so a singleton context would freeze one
        // caller's visibility and serve it to everybody.
        services.AddDbContext<TasksDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Scoped, because it writes through the request-scoped context — and because it is resolved
        // INSIDE another module's scoped handler, which is where every task is recorded from.
        services.AddScoped<ITaskRecorder, TaskRecorder>();

        // The stream's settings, validated at startup like every other options class.
        services.AddOptions<TaskStreamOptions>()
            .Bind(configuration.GetSection(TaskStreamOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singleton: it holds no per-request state, and formats frames into a response it is handed.
        services.AddSingleton<TaskStreamWriter>();

        // Scoped, because it reads through the request-scoped, filtered context. Resolved by
        // TasksController, which is itself scoped.
        services.AddScoped<TaskStreamService>();

        // Scoped, because it writes through the request-scoped context — resolved once per
        // Wolverine message the same way CertificateRenewalHandler is. The Host schedules
        // TaskRetentionRequested; this is what runs when it arrives.
        services.AddScoped<TaskRetentionHandler>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T>
        // directly instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(TasksModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(TasksModule).Assembly));
    }
}
