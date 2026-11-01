using System.Data.Common;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Monitoring.Services;

/// <summary>
/// Applies the administrator's stored log level when the panel starts, so a choice made at runtime
/// survives a restart (issue #131).
/// </summary>
/// <remarks>
/// <para>
/// A hosted service, so the Host registers it (rules/csharp.md: a module may not); it lives here
/// because what it reads is this module's table. It runs in <see cref="StartAsync"/>, before the
/// panel serves a request, so no line is written at the configured level after an administrator
/// chose another one — except the start-up lines that precede it, which is the order the host
/// builds in and cannot be otherwise.
/// </para>
/// <para>
/// <b>No row means no choice</b>, and the configured level stays in force. <b>An unreadable table
/// is not a reason to refuse to start</b>: a database that is not migrated, not reachable or not
/// configured is
/// reported by the health endpoint, and the panel running at its configured level is the right
/// outcome for a log setting. That failure is logged at Warning and the start continues.
/// </para>
/// </remarks>
public sealed class StartupLogLevelRestorer : IHostedService
{
    /// <summary>The line written when a stored level is applied.</summary>
    private static readonly Action<ILogger, PanelLogLevel, Exception?> LogRestored =
        LoggerMessage.Define<PanelLogLevel>(
            LogLevel.Information,
            new EventId(1, nameof(StartupLogLevelRestorer)),
            "Applied the stored panel log level {Level}");

    /// <summary>The line written when the stored level could not be read.</summary>
    private static readonly Action<ILogger, Exception?> LogUnreadable =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(StartupLogLevelRestorer)),
            "Could not read the stored panel log level; the configured level stays in force");

    /// <summary>Creates the scope the database is read through; this service is a singleton.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>The running logger's level.</summary>
    private readonly IPanelLogLevelSwitch _levelSwitch;

    /// <summary>Where the outcome is logged.</summary>
    private readonly ILogger<StartupLogLevelRestorer> _logger;

    /// <summary>Creates the restorer.</summary>
    /// <param name="scopeFactory">Creates the scope the database is read through.</param>
    /// <param name="levelSwitch">The running logger's level.</param>
    /// <param name="logger">Where the outcome is logged.</param>
    public StartupLogLevelRestorer(
        IServiceScopeFactory scopeFactory,
        IPanelLogLevelSwitch levelSwitch,
        ILogger<StartupLogLevelRestorer> logger)
    {
        _scopeFactory = scopeFactory;
        _levelSwitch = levelSwitch;
        _logger = logger;
    }

    /// <summary>Reads the stored level and applies it, when there is one.</summary>
    /// <param name="cancellationToken">Cancels the read when the start is abandoned.</param>
    /// <returns>Resolves once the level is applied, or once there was nothing to apply.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        PanelLogLevel? stored;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
            stored = await dbContext.LogLevelSettings
                .Where(row => row.Id == LogLevelSetting.SingletonId)
                .Select(row => (PanelLogLevel?)row.Level)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            // Handled, not swallowed: the outcome — the configured level stays — is the documented
            // answer to an unreadable table, and the line says so. DbException is a database that
            // answered with a refusal (no table yet, no server); InvalidOperationException is the
            // provider refusing before it asks, which is what a host with no connection string
            // configured gets (the Host's own unit tests boot exactly that host).
            LogUnreadable(_logger, exception);
            return;
        }

        if (stored is { } level)
        {
            _levelSwitch.Apply(level);
            LogRestored(_logger, level, null);
        }
    }

    /// <summary>Nothing to stop: the work is done in <see cref="StartAsync"/>.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
