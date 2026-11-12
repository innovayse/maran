using System.Globalization;
using Maran.Modules.Licensing.Domain.Enums;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Describes the installed licence in one plain line, and this server's id in a second, for the
/// <c>maran status</c> command line. Read-only and decision-free: it reports what
/// <see cref="LicenceCoverageSource"/> and <see cref="LicenceServerFactsSource"/> found, in the words the status
/// screen uses (<see cref="LicenceStatusDisplayNames"/>), and the command that prints it only prints.
/// </summary>
/// <remarks>
/// The licence line never carries the licence text, its signature or the machine id; the server id has a line of
/// its own (<see cref="DescribeServerAsync"/>), the same value the licence screen shows, so an operator at the
/// console can name this server to the marketplace without opening a browser. Reads the files fresh on every
/// call, because an operator running the command wants the truth, not a remembered answer.
/// </remarks>
public sealed class LicenceStatusLine
{
    /// <summary>Reads the licence and its lease.</summary>
    private readonly LicenceCoverageSource _coverageSource;

    /// <summary>The words the status screen uses.</summary>
    private readonly LicenceStatusDisplayNames _displayNames;

    /// <summary>Reads this server's id.</summary>
    private readonly LicenceServerFactsSource _serverFacts;

    /// <summary>Says whether the licence is close to its expiry.</summary>
    private readonly LicenceExpiryWarningSource _expiryWarnings;

    /// <summary>Creates the describer.</summary>
    /// <param name="coverageSource">Reads the licence and its lease.</param>
    /// <param name="displayNames">The words the status screen uses.</param>
    /// <param name="serverFacts">Reads this server's id.</param>
    /// <param name="expiryWarnings">Says whether the licence is close to its expiry.</param>
    public LicenceStatusLine(
        LicenceCoverageSource coverageSource,
        LicenceStatusDisplayNames displayNames,
        LicenceServerFactsSource serverFacts,
        LicenceExpiryWarningSource expiryWarnings)
    {
        _coverageSource = coverageSource;
        _displayNames = displayNames;
        _serverFacts = serverFacts;
        _expiryWarnings = expiryWarnings;
    }

    /// <summary>Reads the licence and describes it.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One line, without a line ending.</returns>
    public async Task<string> DescribeAsync(CancellationToken cancellationToken)
    {
        var coverage = await _coverageSource.ReadAsync(cancellationToken);
        var lease = _displayNames.LeaseNameFor(coverage.Lease.Kind);

        return coverage.Status switch
        {
            LicenceStatus.Valid valid =>
                $"{_displayNames.StateNameFor(LicenceStateKind.Valid)}: {valid.Licence.Tier}, "
                + $"{valid.Licence.Expiry.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}; {lease}",
            LicenceStatus.Absent => _displayNames.StateNameFor(LicenceStateKind.Absent),
            LicenceStatus.Refused refused =>
                $"{_displayNames.StateNameFor(LicenceStateKind.Refused)}: {_displayNames.ReasonNameFor(refused.Reason)}",
            _ => throw new InvalidOperationException($"Unknown LicenceStatus case: {coverage.Status}."),
        };
    }

    /// <summary>Reads the licence and, when it is close to its expiry, says so and how to renew it.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One line holding the expiry day, the days left, the renewal steps and, when this server has an id the
    /// marketplace accepts, the claim link; null when no warning stands (no licence, a refused or expired one, or one
    /// not yet within a threshold). No line ending.
    /// </returns>
    public async Task<string?> DescribeExpiryWarningAsync(CancellationToken cancellationToken)
    {
        var coverage = await _coverageSource.ReadAsync(cancellationToken);
        var warning = _expiryWarnings.For(coverage.Status);

        if (warning is null)
        {
            return null;
        }

        var server = await _serverFacts.ReadAsync(cancellationToken);
        var line = $"{_displayNames.ExpiryWarningFor(warning)} {_displayNames.ExpiryRenewalAdvice()}";

        return server.ClaimUrl is null ? line : $"{line} {server.ClaimUrl}";
    }

    /// <summary>Reads this server's id and describes it in one line.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>Server ID: </c> and the id as the licence comparison reads it, or the localized word for "unavailable"
    /// when the host has none or the agent could not be asked; no line ending.
    /// </returns>
    public async Task<string> DescribeServerAsync(CancellationToken cancellationToken)
    {
        var server = await _serverFacts.ReadAsync(cancellationToken);

        return $"{_displayNames.ServerIdLabel()}: {server.ServerId ?? _displayNames.ServerIdUnavailable()}";
    }
}
