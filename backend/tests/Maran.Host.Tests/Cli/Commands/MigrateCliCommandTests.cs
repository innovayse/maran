using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.Tests.Cli.Fixtures;
using Maran.Sdk.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Cli.Commands;

/// <summary>
/// The <c>migrate</c> verb: it asks each module's own <see cref="IModuleMigrations"/>, reports each
/// schema, and refuses before applying anything when contexts and migrations disagree.
/// </summary>
public sealed class MigrateCliCommandTests
{
    /// <summary>Builds the verb over a container holding the sales context and the given migrations.</summary>
    /// <param name="withContext">Whether the sales context is registered.</param>
    /// <param name="migrations">The module migrations to register.</param>
    /// <returns>The verb, with the provider that must outlive it.</returns>
    private static (MigrateCliCommand Command, ServiceProvider Provider) Build(
        bool withContext,
        params IModuleMigrations[] migrations)
    {
        var services = new ServiceCollection();
        if (withContext)
        {
            services.AddDbContext<SalesTestDbContext>(options => { options.UseNpgsql("Host=unused"); });
        }

        foreach (var item in migrations)
        {
            services.AddSingleton(item);
        }

        var provider = services.BuildServiceProvider();
        return (new MigrateCliCommand(provider.GetRequiredService<IServiceScopeFactory>(), provider), provider);
    }

    /// <summary>A pending migration is applied through the module and reported by schema, from and to.</summary>
    [Fact]
    public async Task A_pending_migration_is_applied_through_the_module_and_reported_by_schema()
    {
        var sales = new RecordingModuleMigrations("sales", string.Empty, "20260101_Init");
        var (command, provider) = Build(true, sales);
        using var _ = provider;
        using var console = new CapturedCliConsole();

        var result = await command.ExecuteAsync(new CliInvocation("migrate", [], true, false, null), console.Console, default);

        Assert.Equal(CliExitCode.Success, result.ExitCode);
        Assert.Equal(1, sales.MigrateCalls);
        Assert.Contains("sales: migrated from (none) to 20260101_Init.", console.Output, StringComparison.Ordinal);
    }

    /// <summary>A schema already at its latest migration is reported up to date (inverse of the applied case).</summary>
    [Fact]
    public async Task A_schema_already_current_is_reported_up_to_date()
    {
        var sales = new RecordingModuleMigrations("sales", "20260101_Init", "20260101_Init");
        var (command, provider) = Build(true, sales);
        using var _ = provider;
        using var console = new CapturedCliConsole();

        await command.ExecuteAsync(new CliInvocation("migrate", [], true, false, null), console.Console, default);

        Assert.Contains("sales: up to date.", console.Output, StringComparison.Ordinal);
    }

    /// <summary>A registered context no module migrates stops the run before any module migrates anything.</summary>
    [Fact]
    public async Task A_context_no_module_migrates_stops_the_run_before_anything_is_applied()
    {
        var other = new RecordingModuleMigrations("stock", string.Empty, "20260101_Init");
        var (command, provider) = Build(true, other);
        using var _ = provider;
        using var console = new CapturedCliConsole();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await command.ExecuteAsync(new CliInvocation("migrate", [], true, false, null), console.Console, default);
        });

        Assert.Equal(0, other.MigrateCalls);
    }

    /// <summary>With no context registered at all the verb refuses instead of reporting success over nothing.</summary>
    [Fact]
    public async Task With_no_context_registered_the_verb_refuses()
    {
        var sales = new RecordingModuleMigrations("sales", string.Empty, "20260101_Init");
        var (command, provider) = Build(false, sales);
        using var _ = provider;
        using var console = new CapturedCliConsole();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await command.ExecuteAsync(new CliInvocation("migrate", [], true, false, null), console.Console, default);
        });

        Assert.Equal(0, sales.MigrateCalls);
    }
}
