using Maran.Modules.Licensing.Common;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Services;

namespace Maran.Modules.Licensing.Queries.GetLicenceStatus;

/// <summary>
/// Handles <see cref="GetLicenceStatusQuery"/> by re-verifying the installed licence and its lease and
/// reporting the three-state answer with the lease's standing, then journalling that the fact was read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never <c>Result{T}.Fail</c>.</b> <see cref="LicenceVerifier.VerifyAsync"/> is typed to
/// never throw and to never report anything but a <see cref="LicenceStatus"/> value (spec §229, "the
/// core never dies" — see that type's own remarks), and this handler adds no failure path of its own:
/// it has no identifier to fail to find, no write to conflict on, and no external call to time out on.
/// Reintroducing an exception arm here — say, by letting a future edit route a missing dependency
/// through <c>Result.Fail</c> instead of letting DI resolution fault the request the ordinary way —
/// would be exactly the regression <c>docs/superpowers/notes/2026-09-22-licence-verification-threat-
/// note.md</c> §2 warns a caller of this verifier must not introduce.
/// </para>
/// <para>
/// <b>Re-verifies rather than trusting a cached status.</b> This slice adds no persistence for "the
/// currently installed licence" (see <see cref="LicensingModule"/>'s own remarks), so the only honest
/// answer is the one <see cref="LicenceCoverageSource"/> would give a fresh caller right now, never the
/// cache the request-time gate uses: an administrator reading the screen sees the files as they are.
/// </para>
/// <para>
/// <b>The audit entry is written on every call, never only on a refusal.</b> See
/// <see cref="LicensingAuditJournal"/>'s own remarks for the argument that a read of an
/// installation-wide fact by an administrator belongs in the trail at all, and for exactly what its
/// subject may and may not carry.
/// </para>
/// </remarks>
public sealed class GetLicenceStatusQueryHandler
{
    /// <summary>Reads the installed licence and its lease and judges both now, uncached.</summary>
    private readonly LicenceCoverageSource _coverageSource;

    /// <summary>Resolves the operator-facing sentence and advice for a status.</summary>
    private readonly LicenceStatusDisplayNames _displayNames;

    /// <summary>Writes this module's audit entries.</summary>
    private readonly LicensingAuditJournal _auditJournal;

    /// <summary>Creates the handler.</summary>
    /// <param name="coverageSource">Reads the installed licence and its lease and judges both now, uncached.</param>
    /// <param name="displayNames">Resolves the operator-facing sentence and advice for a status.</param>
    /// <param name="auditJournal">Writes this module's audit entries.</param>
    public GetLicenceStatusQueryHandler(
        LicenceCoverageSource coverageSource,
        LicenceStatusDisplayNames displayNames,
        LicensingAuditJournal auditJournal)
    {
        _coverageSource = coverageSource;
        _displayNames = displayNames;
        _auditJournal = auditJournal;
    }

    /// <summary>Reads, verifies and reports the installed licence's status.</summary>
    /// <param name="query">The status request, carrying only the caller's address and user agent for the audit entry.</param>
    /// <param name="cancellationToken">Cancellation token for the verification and the audit write.</param>
    /// <returns>Always a success: see this type's own remarks for why no failure path exists here.</returns>
    public async Task<Result<LicenceStatusDto>> HandleAsync(
        GetLicenceStatusQuery query,
        CancellationToken cancellationToken)
    {
        var coverage = await _coverageSource.ReadAsync(cancellationToken);

        await _auditJournal.RecordReadAsync(coverage.Status, query.IpAddress, query.UserAgent, cancellationToken);

        return Result<LicenceStatusDto>.Ok(ToDto(coverage));
    }

    /// <summary>Maps the verified status to its wire shape, adding only text already judged safe to disclose.</summary>
    /// <param name="coverage">The three-state outcome <see cref="LicenceVerifier"/> produced and the standing of its lease.</param>
    /// <returns>The DTO — see <see cref="LicenceStatusDto"/>'s own remarks for what it may never carry.</returns>
    private LicenceStatusDto ToDto(LicenceCoverage coverage)
    {
        var status = coverage.Status;
        var leaseName = _displayNames.LeaseNameFor(coverage.Lease.Kind);

        switch (status)
        {
            case LicenceStatus.Valid valid:
                return new LicenceStatusDto(
                    State: LicenceStateKind.Valid,
                    LicenceId: valid.Licence.Id.Value,
                    Tier: valid.Licence.Tier,
                    Modules: valid.Licence.Modules,
                    Expiry: valid.Licence.Expiry,
                    RefusalReason: null,
                    Sentence: _displayNames.SentenceFor(status),
                    Advice: null,
                    StateDisplayName: _displayNames.StateNameFor(LicenceStateKind.Valid),
                    RefusalReasonDisplayName: null,
                    IsOffline: valid.Licence.IsOffline,
                    Lease: coverage.Lease.Kind,
                    LeaseDisplayName: leaseName,
                    LeaseValidUntil: coverage.Lease.ValidUntil);

            case LicenceStatus.Absent:
                return new LicenceStatusDto(
                    State: LicenceStateKind.Absent,
                    LicenceId: null,
                    Tier: null,
                    Modules: null,
                    Expiry: null,
                    RefusalReason: null,
                    Sentence: _displayNames.SentenceFor(status),
                    Advice: null,
                    StateDisplayName: _displayNames.StateNameFor(LicenceStateKind.Absent),
                    RefusalReasonDisplayName: null,
                    IsOffline: false,
                    Lease: coverage.Lease.Kind,
                    LeaseDisplayName: leaseName,
                    LeaseValidUntil: null);

            case LicenceStatus.Refused refused:
                return new LicenceStatusDto(
                    State: LicenceStateKind.Refused,
                    LicenceId: null,
                    Tier: null,
                    Modules: null,
                    Expiry: null,
                    RefusalReason: refused.Reason,
                    Sentence: _displayNames.SentenceFor(status),
                    Advice: _displayNames.AdviceFor(refused.Reason),
                    StateDisplayName: _displayNames.StateNameFor(LicenceStateKind.Refused),
                    RefusalReasonDisplayName: _displayNames.ReasonNameFor(refused.Reason),
                    IsOffline: false,
                    Lease: coverage.Lease.Kind,
                    LeaseDisplayName: leaseName,
                    LeaseValidUntil: null);

            default:
                throw new InvalidOperationException($"Unknown LicenceStatus case: {status}.");
        }
    }
}
