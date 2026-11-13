using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.Interfaces;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// One pass of the licence lease renewal: decide whether a lease is due, ask the licence service for one, judge
/// the answer exactly as the stored file is judged, and store it only if it is a better lease for THIS licence
/// on THIS server. The only code in the panel that writes <c>lease.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A bad answer never replaces a good lease.</b> The answer is stored only when ALL hold: the licence is
/// valid and online; the answer is under the size bound; its envelope parses and its Ed25519 signature verifies
/// against the pinned lease key (<see cref="LeaseEnvelopeReader"/>, the reader of the stored file); it names
/// this licence and, if it names one, this server; it was issued no more than the renewal interval ago and is
/// not in the future; it ends later than the lease already standing; and the licence installed when it is
/// written is still the one it was asked for. Anything else is an outcome and a log line, and the file is not
/// touched. The old lease keeps covering the paid modules for the rest of its life.
/// </para>
/// <para>
/// <b>Sends only the licence id</b> (owner decision 46, option a): see <see cref="LeaseServiceClient"/>.
/// </para>
/// <para>
/// <b>No call for an offline licence, and none without a valid licence.</b> An offline licence needs no lease
/// (<see cref="LeaseGracePolicy"/>), so the service is never contacted for one; a missing, refused or expired
/// licence has nothing to renew a lease for.
/// </para>
/// <para>
/// <b>The write is serialised with licence installs</b> (<see cref="LicenceInstallLock"/>) and re-checks the
/// licence inside the lock, so a lease issued for a licence that an administrator replaced in the meantime is
/// dropped instead of landing beside the new licence. The network call is outside the lock.
/// </para>
/// <para>
/// <b>Never throws</b> for anything the file system, the network or the service does; it returns the outcome.
/// Only cancellation propagates.
/// </para>
/// </remarks>
public sealed class LeaseRenewer
{
    /// <summary>A verified lease could not be written.</summary>
    private static readonly Action<ILogger, string, Exception?> LogWriteFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(LogWriteFailed)),
            "A verified licence lease could not be stored ({ExceptionType}); the previous lease is unchanged.");

    /// <summary>The licence service has no usable address.</summary>
    private static readonly Action<ILogger, Exception?> LogNotConfigured =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(LogNotConfigured)),
            "The licence service address is not a usable https origin, so no lease is renewed.");

    /// <summary>Reads the installed licence's text.</summary>
    private readonly ILicenceRawTextSource _rawTextSource;

    /// <summary>Verifies the installed licence.</summary>
    private readonly LicenceVerifier _verifier;

    /// <summary>Reads the stored lease.</summary>
    private readonly ILicenceLeaseSource _leaseSource;

    /// <summary>Reads this host's machine id.</summary>
    private readonly IServerIdentitySource _serverIdentity;

    /// <summary>Asks the licence service.</summary>
    private readonly ILeaseServiceClient _client;

    /// <summary>Judges the service's answer.</summary>
    private readonly LeaseEnvelopeReader _reader;

    /// <summary>Stores a verified lease.</summary>
    private readonly ILeaseWriter _writer;

    /// <summary>Serialises the write with licence installs.</summary>
    private readonly LicenceInstallLock _installLock;

    /// <summary>Makes the gate see the new lease at once.</summary>
    private readonly ILicenceCoverageInvalidator _invalidator;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Operator log.</summary>
    private readonly ILogger<LeaseRenewer> _logger;

    /// <summary>Creates the renewer.</summary>
    /// <param name="rawTextSource">Reads the installed licence's text.</param>
    /// <param name="verifier">Verifies the installed licence.</param>
    /// <param name="leaseSource">Reads the stored lease.</param>
    /// <param name="serverIdentity">Reads this host's machine id.</param>
    /// <param name="client">Asks the licence service.</param>
    /// <param name="reader">Judges the service's answer.</param>
    /// <param name="writer">Stores a verified lease.</param>
    /// <param name="installLock">Serialises the write with licence installs.</param>
    /// <param name="invalidator">Makes the gate see the new lease at once.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="logger">Operator log.</param>
    public LeaseRenewer(
        ILicenceRawTextSource rawTextSource,
        LicenceVerifier verifier,
        ILicenceLeaseSource leaseSource,
        IServerIdentitySource serverIdentity,
        ILeaseServiceClient client,
        LeaseEnvelopeReader reader,
        ILeaseWriter writer,
        LicenceInstallLock installLock,
        ILicenceCoverageInvalidator invalidator,
        IClock clock,
        ILogger<LeaseRenewer> logger)
    {
        _rawTextSource = rawTextSource;
        _verifier = verifier;
        _leaseSource = leaseSource;
        _serverIdentity = serverIdentity;
        _client = client;
        _reader = reader;
        _writer = writer;
        _installLock = installLock;
        _invalidator = invalidator;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs one renewal pass for whichever licence is installed.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the pass did.</returns>
    public Task<LeaseRenewalResult> RenewAsync(CancellationToken cancellationToken)
    {
        return RenewAsync(null, cancellationToken);
    }

    /// <summary>
    /// Runs one renewal pass, for one named licence when the caller names it: an install asks about the licence it
    /// has just installed, and a second install that replaced it in the meantime must not have its licence leased
    /// under the first caller's name.
    /// </summary>
    /// <param name="onlyLicenceId">The licence the caller means, or null for whichever is installed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the pass did; <see cref="LeaseRenewalOutcome.LicenceChanged"/> when the installed licence is not the named one.</returns>
    public async Task<LeaseRenewalResult> RenewAsync(string? onlyLicenceId, CancellationToken cancellationToken)
    {
        var status = await _verifier.VerifyAsync(_rawTextSource.ReadRawLicenceText(), cancellationToken);
        if (status is not LicenceStatus.Valid valid)
        {
            return LeaseRenewalResult.Of(LeaseRenewalOutcome.SkippedNoLicence);
        }

        var licence = valid.Licence;
        if (onlyLicenceId is not null && !string.Equals(licence.Id.Value, onlyLicenceId, StringComparison.Ordinal))
        {
            return LeaseRenewalResult.Of(LeaseRenewalOutcome.LicenceChanged);
        }

        if (licence.IsOffline)
        {
            return LeaseRenewalResult.Of(LeaseRenewalOutcome.SkippedOffline);
        }

        var stored = await _leaseSource.ReadAsync(cancellationToken);
        if (!LeaseRenewalPolicy.IsDue(stored, licence, _clock.UtcNow))
        {
            return LeaseRenewalResult.Of(LeaseRenewalOutcome.NotDue);
        }

        var answer = await _client.RequestAsync(licence.Id.Value, cancellationToken);
        if (answer.Kind == LeaseServiceAnswerKind.NotConfigured)
        {
            LogNotConfigured(_logger, null);
        }

        if (answer.Kind != LeaseServiceAnswerKind.Received)
        {
            return LeaseRenewalResult.Of(OutcomeOf(answer.Kind));
        }

        var envelope = _reader.Read(answer.Body!);
        if (envelope.Reading is not LeaseReading.Present candidate)
        {
            return LeaseRenewalResult.Of(LeaseRenewalOutcome.AnswerRejected);
        }

        var machineId = await _serverIdentity.TryReadMachineIdAsync(cancellationToken);
        var standing = LeaseGracePolicy.StandingOf(candidate, licence, machineId, _clock.UtcNow);
        if (standing.Kind != LeaseStandingKind.Fresh)
        {
            return LeaseRenewalResult.Of(LeaseRenewalOutcome.AnswerRejected);
        }

        return await _installLock.RunExclusiveAsync(
            token => { return StoreAsync(licence.Id.Value, machineId, candidate.Lease, answer.Body!, token); },
            cancellationToken);
    }

    /// <summary>Maps an answer that carried no body to what the pass amounted to.</summary>
    /// <param name="kind">Any kind but <see cref="LeaseServiceAnswerKind.Received"/>.</param>
    /// <returns>The outcome.</returns>
    private static LeaseRenewalOutcome OutcomeOf(LeaseServiceAnswerKind kind)
    {
        return kind switch
        {
            LeaseServiceAnswerKind.NotConfigured => LeaseRenewalOutcome.NotConfigured,
            LeaseServiceAnswerKind.Unreachable => LeaseRenewalOutcome.Unreachable,
            LeaseServiceAnswerKind.Refused => LeaseRenewalOutcome.Refused,
            LeaseServiceAnswerKind.TooLarge => LeaseRenewalOutcome.AnswerRejected,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Received has a body and is judged, not mapped."),
        };
    }

    /// <summary>Stores the verified lease if it is still the right one, inside the install lock.</summary>
    /// <param name="licenceId">The licence the service was asked about.</param>
    /// <param name="machineId">This host's machine id, as read for the judgement.</param>
    /// <param name="lease">The verified lease.</param>
    /// <param name="envelopeText">The envelope exactly as it was verified.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Renewed, or why nothing was written.</returns>
    private async Task<LeaseRenewalResult> StoreAsync(
        string licenceId,
        string? machineId,
        LicenceLease lease,
        string envelopeText,
        CancellationToken cancellationToken)
    {
        // The administrator may have replaced the licence while the service was being asked.
        var current = await _verifier.VerifyAsync(_rawTextSource.ReadRawLicenceText(), cancellationToken);
        if (current is not LicenceStatus.Valid valid
            || !string.Equals(valid.Licence.Id.Value, licenceId, StringComparison.Ordinal))
        {
            return LeaseRenewalResult.Of(LeaseRenewalOutcome.LicenceChanged);
        }

        var now = _clock.UtcNow;
        var storedStanding = LeaseGracePolicy.StandingOf(
            await _leaseSource.ReadAsync(cancellationToken),
            valid.Licence,
            machineId,
            now);
        if (!LeaseRenewalPolicy.Improves(storedStanding, lease))
        {
            return LeaseRenewalResult.Of(LeaseRenewalOutcome.NotNewer);
        }

        try
        {
            await _writer.WriteAsync(envelopeText, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(_logger, exception.GetType().Name, null);

            return LeaseRenewalResult.Of(LeaseRenewalOutcome.WriteFailed);
        }

        // The gate remembers a verdict for thirty seconds; the new lease is seen by the next request.
        _invalidator.Invalidate();

        return new LeaseRenewalResult(LeaseRenewalOutcome.Renewed, lease.ValidUntil);
    }
}
