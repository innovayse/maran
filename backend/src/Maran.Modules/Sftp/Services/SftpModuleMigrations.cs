using Maran.Modules.Sftp.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sftp.Services;

/// <summary>
/// Applies and reports the Sftp module's own schema migrations (<c>sftp</c>), so the Host's migrate verb
/// asks the module for them instead of discovering its context (rules/architecture.md "Each module owns one
/// PostgreSQL schema"). Holds no state: each call opens its own scope and resolves the module's context
/// inside it, which is what lets it be registered as a singleton and called with the root provider.
/// </summary>
public sealed class SftpModuleMigrations : IModuleMigrations
{
    /// <inheritdoc />
    public string SchemaName
    {
        get
        {
            return SftpDbContext.SchemaName;
        }
    }

    /// <inheritdoc />
    public async Task MigrateAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SftpDbContext>();
        await context.Database.MigrateAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> CurrentMigrationAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SftpDbContext>();
        var applied = await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false);
        return applied.LastOrDefault() ?? string.Empty;
    }
}
