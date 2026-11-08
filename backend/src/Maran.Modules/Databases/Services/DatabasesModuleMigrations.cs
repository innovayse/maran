using Maran.Modules.Databases.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Databases.Services;

/// <summary>
/// Applies and reports the Databases module's own schema migrations (<c>databases</c>), so the Host's migrate verb
/// asks the module for them instead of discovering its context (rules/architecture.md "Each module owns one
/// PostgreSQL schema"). Holds no state: each call opens its own scope and resolves the module's context
/// inside it, which is what lets it be registered as a singleton and called with the root provider.
/// </summary>
public sealed class DatabasesModuleMigrations : IModuleMigrations
{
    /// <inheritdoc />
    public string SchemaName
    {
        get
        {
            return DatabasesDbContext.SchemaName;
        }
    }

    /// <inheritdoc />
    public async Task MigrateAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabasesDbContext>();
        await context.Database.MigrateAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> CurrentMigrationAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabasesDbContext>();
        var applied = await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false);
        return applied.LastOrDefault() ?? string.Empty;
    }
}
