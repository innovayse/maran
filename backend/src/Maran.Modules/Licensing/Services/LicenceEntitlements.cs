using Maran.Modules.Licensing.Domain.Policies;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// The Licensing module's implementation of the panel-wide licence window
/// (<see cref="ILicenceEntitlements"/>): it asks <see cref="LicenceCoverageCache"/> for the licence and
/// lease and applies <see cref="ModuleCoveragePolicy"/>, the one rule.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never throws, and fails closed.</b> If reading the licence fails for any reason other than the
/// caller cancelling, every requested module is answered <see cref="EntitlementVerdict.NotCovered"/>:
/// a paid module is refused, and the rest of the panel, which never asks, is untouched.
/// </para>
/// <para>
/// <b>Logged once.</b> The first failure after a healthy spell is logged at error level and the
/// recovery at information level; a failure that persists is not logged again on every request, so a
/// broken licence directory cannot flood the log.
/// </para>
/// </remarks>
public sealed class LicenceEntitlements : ILicenceEntitlements
{
    /// <summary>The first failure after a healthy spell.</summary>
    private static readonly Action<ILogger, string, Exception?> LogReadFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(LogReadFailed)),
            "Reading the licence failed ({ExceptionType}); paid modules are refused until it can be read.");

    /// <summary>The first success after a failure.</summary>
    private static readonly Action<ILogger, Exception?> LogReadRecovered =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, nameof(LogReadRecovered)),
            "The licence can be read again.");

    /// <summary>The remembered licence and lease.</summary>
    private readonly LicenceCoverageCache _cache;

    /// <summary>Operator-facing log.</summary>
    private readonly ILogger<LicenceEntitlements> _logger;

    /// <summary>1 while the last read failed, 0 otherwise; makes the failure log once.</summary>
    private int _failing;

    /// <summary>Creates the window.</summary>
    /// <param name="cache">The remembered licence and lease.</param>
    /// <param name="logger">Operator-facing log.</param>
    public LicenceEntitlements(LicenceCoverageCache cache, ILogger<LicenceEntitlements> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ModuleEntitlement> GetAsync(string moduleId, CancellationToken cancellationToken)
    {
        var list = await ListAsync([moduleId], cancellationToken);

        return list[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModuleEntitlement>> ListAsync(
        IReadOnlyCollection<string> moduleIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var coverage = await _cache.GetAsync(cancellationToken);
            RecordRecovery();

            return moduleIds
                .Select(moduleId => { return ModuleCoveragePolicy.VerdictOf(coverage, moduleId); })
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);

            return moduleIds
                .Select(moduleId => { return new ModuleEntitlement(moduleId, EntitlementVerdict.NotCovered, null); })
                .ToList();
        }
    }

    /// <summary>Logs the first failure after a healthy spell.</summary>
    /// <param name="exception">What failed; only its type is logged.</param>
    private void RecordFailure(Exception exception)
    {
        if (Interlocked.Exchange(ref _failing, 1) == 0)
        {
            LogReadFailed(_logger, exception.GetType().Name, null);
        }
    }

    /// <summary>Logs the first success after a failure.</summary>
    private void RecordRecovery()
    {
        if (Interlocked.Exchange(ref _failing, 0) == 1)
        {
            LogReadRecovered(_logger, null);
        }
    }
}
