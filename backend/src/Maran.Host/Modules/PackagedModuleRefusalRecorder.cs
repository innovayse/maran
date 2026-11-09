using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Modules;

/// <summary>
/// Records, once per start, every packaged module the start refused, so that "the panel came up without
/// your module" is a line in the audit journal and not only a log entry nobody reads.
/// </summary>
/// <remarks>
/// A refusal is an event worth reading (<see cref="MarketplaceAuditActions.ModuleRefused"/>), and this
/// is the only place one is born that the loader did not witness: the loader verified the package when
/// it was installed, the composition judged it again at this start. Writing the journal must not be the
/// thing that stops the panel, so a failure here is logged and the start goes on.
/// </remarks>
public sealed class PackagedModuleRefusalRecorder : IHostedService
{
    /// <summary>The breadcrumb left when a refusal could not be journalled.</summary>
    private static readonly Action<ILogger, Exception?> LogRefusalUnrecorded =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(PackagedModuleRefusalRecorder)),
            "A refused packaged module could not be written to the audit journal; the panel started without it.");

    /// <summary>What this start composed and refused.</summary>
    private readonly PackagedModuleComposition _composition;

    /// <summary>Opens the scope the journal write needs.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where a failed write is noted.</summary>
    private readonly ILogger<PackagedModuleRefusalRecorder> _logger;

    /// <summary>Creates the recorder.</summary>
    /// <param name="composition">What this start composed and refused.</param>
    /// <param name="scopeFactory">Opens the scope the journal write runs in.</param>
    /// <param name="logger">Where a failed write is noted.</param>
    public PackagedModuleRefusalRecorder(
        PackagedModuleComposition composition,
        IServiceScopeFactory scopeFactory,
        ILogger<PackagedModuleRefusalRecorder> logger)
    {
        _composition = composition;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_composition.Refused.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var journal = scope.ServiceProvider.GetRequiredService<IModuleLoaderAudit>();

            foreach (var refusal in _composition.Refused)
            {
                await journal.RecordAsync(
                    MarketplaceAuditActions.ModuleRefused,
                    refusal.Manifest?.Id,
                    refusal.Manifest?.Version,
                    refusal.Reason,
                    false,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Deliberately broad, with the reason on the line: the audit must not be the thing that
            // keeps the panel from starting. A schema that is not migrated yet, a database that is
            // down - none of them is a reason to take the control panel with them.
            LogRefusalUnrecorded(_logger, exception);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
