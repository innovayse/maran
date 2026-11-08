using Maran.Modules.Backups.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Brings the <c>backups</c> schema up to date for the Host's migrate verb, without the Host knowing
/// <see cref="BackupsDbContext"/> (rules/architecture.md: each module owns one schema and its own migrations).
/// </summary>
/// <remarks>
/// Registered as a singleton and holding no state: every call opens a scope of its own, because the
/// context is scoped (its tenant filter closes over the request's caller) and a migration run has no
/// request. Applying migrations stays the job of <c>maran migrate</c>; this type is how that verb
/// reaches the module, not a second moment at which the schema changes.
/// </remarks>
public sealed class BackupsModuleMigrations : IModuleMigrations
{
    /// <inheritdoc />
    public string SchemaName
    {
        get
        {
            return BackupsDbContext.SchemaName;
        }
    }

    /// <inheritdoc />
    public async Task MigrateAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        await context.Database.MigrateAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> CurrentMigrationAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var applied = await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false);
        return applied.LastOrDefault() ?? string.Empty;
    }
}
