using Maran.Modules.Firewall.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Firewall.Services;

/// <summary>
/// Brings the <c>firewall</c> schema up to date for the Host's migrate verb, without the Host knowing
/// <see cref="FirewallDbContext"/> (rules/architecture.md: each module owns one schema and its own migrations).
/// </summary>
/// <remarks>
/// Registered as a singleton and holding no state: every call opens a scope of its own, because the
/// context is scoped (its tenant filter closes over the request's caller) and a migration run has no
/// request. Applying migrations stays the job of <c>maran migrate</c>; this type is how that verb
/// reaches the module, not a second moment at which the schema changes.
/// </remarks>
public sealed class FirewallModuleMigrations : IModuleMigrations
{
    /// <inheritdoc />
    public string SchemaName
    {
        get
        {
            return FirewallDbContext.SchemaName;
        }
    }

    /// <inheritdoc />
    public async Task MigrateAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
        await context.Database.MigrateAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> CurrentMigrationAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
        var applied = await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false);
        return applied.LastOrDefault() ?? string.Empty;
    }
}
