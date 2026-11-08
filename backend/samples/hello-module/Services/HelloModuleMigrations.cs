using Maran.Samples.Hello.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Samples.Hello.Services;

/// <summary>
/// Applies and reports the sample module's own schema migrations (<c>hello</c>), so the panel's migrate
/// verb asks the module for them instead of discovering its context. Holds no state: each call opens
/// its own scope and resolves the context inside it, which is what lets it be registered as a singleton
/// and called with the root provider.
/// </summary>
public sealed class HelloModuleMigrations : IModuleMigrations
{
    /// <inheritdoc />
    public string SchemaName
    {
        get
        {
            return HelloDbContext.SchemaName;
        }
    }

    /// <inheritdoc />
    public async Task MigrateAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<HelloDbContext>();
        await context.Database.MigrateAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> CurrentMigrationAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<HelloDbContext>();
        var applied = await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false);
        return applied.LastOrDefault() ?? string.Empty;
    }
}
