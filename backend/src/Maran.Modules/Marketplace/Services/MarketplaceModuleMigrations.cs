using Maran.Modules.Marketplace.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Applies and reports the Marketplace module's own schema migrations (<c>marketplace</c>), so the Host's
/// migrate verb asks the module for them instead of discovering its context (rules/architecture.md "Each
/// module owns one PostgreSQL schema"). Holds no state: each call opens its own scope and resolves the
/// module's context inside it, which is what lets it be registered as a singleton and called with the
/// root provider.
/// </summary>
public sealed class MarketplaceModuleMigrations : IModuleMigrations
{
    /// <inheritdoc />
    public string SchemaName
    {
        get
        {
            return MarketplaceDbContext.SchemaName;
        }
    }

    /// <inheritdoc />
    public async Task MigrateAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        await context.Database.MigrateAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> CurrentMigrationAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var applied = await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false);
        return applied.LastOrDefault() ?? string.Empty;
    }
}
