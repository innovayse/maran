using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Host.Cli.Services;
using Maran.Sdk.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// Applies every module's pending migrations, in one pass, by asking each module's own
/// <see cref="IModuleMigrations"/>, and reports each schema by name. This is the body <c>--migrate</c> always had, moved here so the installer's flag and
/// <c>--cli migrate</c> are one code path.
/// </summary>
/// <remarks>
/// <para>
/// rules/architecture.md says migrations are never applied by a starting process; the installer and
/// the update command apply them deliberately by asking this verb, which exits without serving
/// anything. A normal start migrates nothing.
/// </para>
/// <para>
/// The modules migrate themselves; this verb does not reach into their contexts to do it. The
/// registered contexts are still discovered, but only as a cross-check
/// (<see cref="ModuleMigrationCoverage"/>): a context no module migrates, or a module migrating a
/// schema no context maps, stops the run before anything is applied.
/// </para>
/// <para>
/// A failure is not swallowed: it propagates to <see cref="CliRunner"/>, the process exits non-zero,
/// and the installer stops on it. An install that could not create the schema must fail where the
/// operator can see it, not sixty seconds later as an HTTP 500 with a correlation id.
/// </para>
/// </remarks>
public sealed class MigrateCliCommand : ICliCommand
{
    /// <summary>Opens the scope each context is resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>The root provider the registered contexts are discovered from.</summary>
    private readonly IServiceProvider _services;

    /// <summary>Creates the verb.</summary>
    /// <param name="scopeFactory">Opens a scope per context and per module, because a context is scoped.</param>
    /// <param name="services">The root provider, from which the registered contexts are discovered.</param>
    public MigrateCliCommand(IServiceScopeFactory scopeFactory, IServiceProvider services)
    {
        _scopeFactory = scopeFactory;
        _services = services;
    }

    /// <inheritdoc />
    public string Verb
    {
        get { return CliInvocationParser.MigrateVerb; }
    }

    /// <inheritdoc />
    public async Task<CliResult> ExecuteAsync(
        CliInvocation invocation,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        var contextTypes = PanelDbContextDiscovery.Discover(_services);

        // A vacuity guard, because "nothing to migrate" and "nothing was found to migrate" look
        // identical in a log and only one of them is good news (rules/testing.md).
        if (contextTypes.Count == 0)
        {
            throw new InvalidOperationException(
                "--migrate found no registered DbContext at all, so it would report success having " +
                "migrated nothing. Refusing rather than leaving an empty schema behind.");
        }

        var contextSchemas = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var contextType in contextTypes)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            contextSchemas[contextType.FullName ?? contextType.Name] = context.Model.GetDefaultSchema();
        }

        List<string> schemas;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            schemas = scope.ServiceProvider.GetServices<IModuleMigrations>()
                .Select(migrations => { return migrations.SchemaName; })
                .ToList();
        }

        ModuleMigrationCoverage.Verify(contextSchemas, schemas);

        foreach (var schema in schemas.OrderBy(name => { return name; }, StringComparer.Ordinal))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var migrations = scope.ServiceProvider.GetServices<IModuleMigrations>()
                .Single(candidate => { return string.Equals(candidate.SchemaName, schema, StringComparison.Ordinal); });

            var before = await migrations.CurrentMigrationAsync(scope.ServiceProvider, cancellationToken)
                .ConfigureAwait(false);
            await migrations.MigrateAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            var after = await migrations.CurrentMigrationAsync(scope.ServiceProvider, cancellationToken)
                .ConfigureAwait(false);

            var line = string.Equals(before, after, StringComparison.Ordinal)
                ? $"{schema}: up to date."
                : $"{schema}: migrated from {Named(before)} to {Named(after)}.";
            await console.Output.WriteLineAsync(line);
        }

        return new CliResult(CliExitCode.Success);
    }

    /// <summary>Spells a migration id for the report, naming the empty schema instead of printing nothing.</summary>
    /// <param name="migration">The migration id <see cref="IModuleMigrations.CurrentMigrationAsync"/> returned, empty when none applied.</param>
    /// <returns>The id, or <c>(none)</c>.</returns>
    private static string Named(string migration)
    {
        return migration.Length == 0 ? "(none)" : migration;
    }
}
