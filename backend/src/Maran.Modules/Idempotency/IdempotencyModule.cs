using System.Resources;
using Maran.Modules.Idempotency.Jobs;
using Maran.Modules.Idempotency.Persistence;
using Maran.Modules.Idempotency.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Idempotency;

/// <summary>
/// The Idempotency module's entry point (rules/csharp.md "Canonical backend layout"). Owns the store
/// behind the Sdk's opt-in <c>[Idempotent]</c> action filter: a retried request that carries an
/// <c>Idempotency-Key</c> is answered with the first response instead of running twice (issue #136).
/// </summary>
/// <remarks>
/// It is a facility more than one module's endpoints use, so it is a module of its own and not a lodger
/// in the first module that wanted it (rules/architecture.md "Backend: modular monolith"); consumers
/// reach it through <see cref="IIdempotencyStore"/> in the Sdk and never through this project. It has no
/// controller and no permission: the filter it serves runs inside another module's action, after that
/// action's own authorization.
/// </remarks>
public sealed class IdempotencyModule : IPanelModule
{
    /// <summary>The connection string every module context reads.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Idempotency.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Idempotency.Resources.DisplayNames";

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
            return IdempotencyManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        services.AddDbContext<IdempotencyDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Scoped: the store shares the request's context, and the filter that calls it is per request.
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();

        // Scoped, like every Wolverine handler this panel resolves from the container; it runs when the
        // Host's hourly ExpiredRecordPurgeRequested arrives.
        services.AddScoped<ExpiredRecordPurgeHandler>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. The codes the Sdk filter raises (IdempotencyKeyInvalid and
        // its three siblings) are defined here.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(IdempotencyModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(IdempotencyModule).Assembly));
    }
}
