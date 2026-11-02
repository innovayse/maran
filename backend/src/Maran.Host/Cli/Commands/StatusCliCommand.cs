using System.Globalization;
using System.Reflection;
using Maran.Agent.Client.Interfaces;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Host.Cli.Services;
using Maran.Host.Extensions;
using Maran.Host.HealthChecks;
using Maran.Host.HealthChecks.Enums;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// Reports what this panel can see of itself: database, agent handshake, pending migrations, its own
/// version, how many administrators exist, and whether the first-run setup window is open. Every check
/// is independent, so a dead database does not hide the agent's answer, and the verb ends by stating
/// how many checks it ran.
/// </summary>
/// <remarks>
/// <para>
/// <b>The count is the vacuity guard.</b> A status that printed nothing because every section was
/// skipped would read as healthy. The verb names the number of checks it ran and fails when that is
/// not the number it owns, so a section that silently stopped running is a failure and not a quiet
/// pass.
/// </para>
/// <para>
/// <b>It prints no secret.</b> The setup section says whether the window is open and never the token.
/// </para>
/// </remarks>
public sealed class StatusCliCommand : ICliCommand
{
    /// <summary>The number of checks this verb owns; fewer than this ran means a check went missing.</summary>
    public const int ExpectedCheckCount = 6;

    /// <summary>Opens the scope the checks resolve their services in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Discovers the registered contexts for the pending-migrations check.</summary>
    private readonly IServiceProvider _services;

    /// <summary>Creates the verb.</summary>
    /// <param name="scopeFactory">Opens a scope per run.</param>
    /// <param name="services">The root provider, from which contexts are discovered.</param>
    public StatusCliCommand(IServiceScopeFactory scopeFactory, IServiceProvider services)
    {
        _scopeFactory = scopeFactory;
        _services = services;
    }

    /// <inheritdoc />
    public string Verb
    {
        get { return "status"; }
    }

    /// <inheritdoc />
    public async Task<CliResult> ExecuteAsync(
        CliInvocation invocation,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var checks = new List<(string Name, Func<Task<(bool Ok, string Detail)>> Run)>
        {
            ("database", () => { return CheckDatabaseAsync(provider); }),
            ("agent", () => { return CheckAgentAsync(provider, cancellationToken); }),
            ("migrations", () => { return CheckMigrationsAsync(cancellationToken); }),
            ("api", () => { return Task.FromResult(CheckApiVersion()); }),
            ("administrators", () => { return CheckAdministratorsAsync(provider, cancellationToken); }),
            ("setup-window", () => { return CheckSetupWindowAsync(provider, cancellationToken); }),
        };

        var passed = 0;
        foreach (var (name, run) in checks)
        {
            (bool Ok, string Detail) outcome;
            try
            {
                outcome = await run();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Deliberately broad: one failing check degrades to a FAIL line so the remaining checks still run.
                // Cancellation is not swallowed: the caller's own cancellation is rethrown by the clause above.
                // The type only: a message from a failed connection can quote the connection string.
                outcome = (false, $"check failed ({exception.GetType().Name})");
            }

            passed += outcome.Ok ? 1 : 0;
            await console.Output.WriteLineAsync($"{(outcome.Ok ? "ok  " : "FAIL")} {name}: {outcome.Detail}");
        }

        await console.Output.WriteLineAsync(
            string.Create(CultureInfo.InvariantCulture, $"{passed} of {checks.Count} checks ok."));

        var complete = checks.Count == ExpectedCheckCount;
        if (!complete)
        {
            await console.Error.WriteLineAsync(
                "The status verb ran fewer checks than it owns; treating that as a failure.");
        }

        return new CliResult(complete && passed == checks.Count ? CliExitCode.Success : CliExitCode.Failure);
    }

    /// <summary>Asks whether the database answers and carries the panel's schema.</summary>
    /// <param name="provider">The scope's provider.</param>
    /// <returns>Whether it is healthy, and the probe's word for it.</returns>
    private static async Task<(bool Ok, string Detail)> CheckDatabaseAsync(IServiceProvider provider)
    {
        var state = await provider.GetRequiredService<DatabaseHealthProbe>().ProbeAsync();
        return (state == DatabaseHealth.Reachable, state.Word());
    }

    /// <summary>Performs the agent handshake and reports its version and contract revision.</summary>
    /// <param name="provider">The scope's provider.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>Whether the agent is connected, and its version and proto revision.</returns>
    private static async Task<(bool Ok, string Detail)> CheckAgentAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var state = await provider.GetRequiredService<AgentHealthProbe>().ProbeAsync(cancellationToken);
        if (state != AgentHealth.Connected)
        {
            return (false, state.Word());
        }

        var info = await provider.GetRequiredService<IAgentSystemClient>().GetInfoAsync(cancellationToken);
        return info.IsSuccess
            ? (true, $"{state.Word()}, version {info.Value.Version}, proto {info.Value.ProtoVersion}")
            : (true, state.Word());
    }

    /// <summary>Counts the migrations still to apply across every registered context.</summary>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>Whether none are pending, and the count.</returns>
    private async Task<(bool Ok, string Detail)> CheckMigrationsAsync(CancellationToken cancellationToken)
    {
        var contextTypes = PanelDbContextDiscovery.Discover(_services);
        if (contextTypes.Count == 0)
        {
            return (false, "no database context is registered, so nothing could be counted");
        }

        var pending = 0;
        foreach (var contextType in contextTypes)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            pending += (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
        }

        return (pending == 0, string.Create(
            CultureInfo.InvariantCulture,
            $"{pending} pending across {contextTypes.Count} context(s)"));
    }

    /// <summary>Reports the version of the running panel build.</summary>
    /// <returns>Always healthy; the detail is the build's informational version.</returns>
    private static (bool Ok, string Detail) CheckApiVersion()
    {
        var version = typeof(StatusCliCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(StatusCliCommand).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        return (true, version);
    }

    /// <summary>Counts active administrators.</summary>
    /// <param name="provider">The scope's provider.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>Healthy when at least one exists, and the count.</returns>
    private static async Task<(bool Ok, string Detail)> CheckAdministratorsAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var identity = provider.GetRequiredService<IdentityDbContext>();
        var count = await identity.Users
            .AsNoTracking()
            .CountAsync(
                user => user.Role == UserRole.Admin && user.Lifecycle == UserLifecycle.Active,
                cancellationToken);
        return (count > 0, string.Create(CultureInfo.InvariantCulture, $"{count} active"));
    }

    /// <summary>Reports whether the first-run setup token can still claim the panel. Never prints the token.</summary>
    /// <param name="provider">The scope's provider.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>Healthy always; the detail is open or closed and why.</returns>
    private static async Task<(bool Ok, string Detail)> CheckSetupWindowAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var identity = provider.GetRequiredService<IdentityDbContext>();
        var hasAdministrator = await identity.Users
            .AsNoTracking()
            .AnyAsync(user => user.Role == UserRole.Admin, cancellationToken);
        if (hasAdministrator)
        {
            return (true, "closed (an administrator exists)");
        }

        var token = provider.GetRequiredService<IOptions<SetupOptions>>().Value.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            return (true, "closed (no setup token is configured)");
        }

        var window = await identity.SetupTokenWindows.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var clock = provider.GetRequiredService<IClock>();
        if (window is not null && clock.UtcNow >= window.OpenedAt + SetupTokenWindowKeeper.Window)
        {
            return (true, "closed (the setup token has expired)");
        }

        return (true, "open (no administrator exists yet)");
    }
}
