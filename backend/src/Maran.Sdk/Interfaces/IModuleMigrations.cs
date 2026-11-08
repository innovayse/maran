namespace Maran.Sdk.Interfaces;

/// <summary>
/// What a module that owns a schema offers the Host so it can bring that schema up to date, without
/// the Host knowing the module's <c>DbContext</c> type.
/// </summary>
/// <remarks>
/// A module with no tables (<see cref="Contracts.Manifest.SchemaName"/> is null) does not implement
/// this. Applying migrations stays the job of the <c>maran migrate</c> verb: this interface is how
/// that verb reaches a module, not a second moment at which a schema changes. A module implements it
/// as a registered service, so its own context and its own migration history stay inside the module.
/// </remarks>
public interface IModuleMigrations
{
    /// <summary>
    /// The PostgreSQL schema this module owns. Equals the module's <see cref="Contracts.Manifest.SchemaName"/>
    /// and therefore its <see cref="Contracts.Manifest.Id"/>.
    /// </summary>
    string SchemaName { get; }

    /// <summary>Applies every migration the module's context has not applied yet. Idempotent.</summary>
    /// <param name="services">A service provider to resolve the module's own context from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the schema is up to date.</returns>
    Task MigrateAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>Reads the id of the last migration applied to the module's schema.</summary>
    /// <param name="services">A service provider to resolve the module's own context from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The last applied migration's id, or the empty string when none has been applied.</returns>
    Task<string> CurrentMigrationAsync(IServiceProvider services, CancellationToken ct);
}
