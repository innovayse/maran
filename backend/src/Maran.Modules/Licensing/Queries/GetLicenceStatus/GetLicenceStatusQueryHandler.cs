using Maran.Modules.Licensing.Common;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Services;

namespace Maran.Modules.Licensing.Queries.GetLicenceStatus;

/// <summary>
/// Handles <see cref="GetLicenceStatusQuery"/> by re-verifying the installed licence and reporting the
/// three-state answer, then journalling that the fact was read.
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
/// answer is the one <see cref="ILicenceRawTextSource"/> and <see cref="LicenceVerifier"/> would give a
/// fresh caller right now — identical to what <see cref="Services.LicenceStartupCheck"/> already does
/// at boot, on the same read-only source.
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
    /// <summary>Where the installed licence's raw text, if any, is read from.</summary>
    private readonly ILicenceRawTextSource _rawTextSource;

    /// <summary>This module's offline verifier.</summary>
    private readonly LicenceVerifier _verifier;

    /// <summary>Resolves the operator-facing sentence and advice for a status.</summary>
    private readonly LicenceStatusDisplayNames _displayNames;

    /// <summary>Writes this module's audit entries.</summary>
    private readonly LicensingAuditJournal _auditJournal;

    /// <summary>Creates the handler.</summary>
    /// <param name="rawTextSource">Where the installed licence's raw text, if any, is read from.</param>
    /// <param name="verifier">This module's offline verifier.</param>
    /// <param name="displayNames">Resolves the operator-facing sentence and advice for a status.</param>
    /// <param name="auditJournal">Writes this module's audit entries.</param>
    public GetLicenceStatusQueryHandler(
        ILicenceRawTextSource rawTextSource,
        LicenceVerifier verifier,
        LicenceStatusDisplayNames displayNames,
        LicensingAuditJournal auditJournal)
    {
        _rawTextSource = rawTextSource;
        _verifier = verifier;
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
        var rawLicenceText = _rawTextSource.ReadRawLicenceText();
        var status = await _verifier.VerifyAsync(rawLicenceText, cancellationToken);

        await _auditJournal.RecordReadAsync(status, query.IpAddress, query.UserAgent, cancellationToken);

        return Result<LicenceStatusDto>.Ok(ToDto(status));
    }

    /// <summary>Maps the verified status to its wire shape, adding only text already judged safe to disclose.</summary>
    /// <param name="status">The three-state outcome <see cref="LicenceVerifier"/> produced.</param>
    /// <returns>The DTO — see <see cref="LicenceStatusDto"/>'s own remarks for what it may never carry.</returns>
    private LicenceStatusDto ToDto(LicenceStatus status)
    {
        switch (status)
        {
            case LicenceStatus.Valid valid:
                return new LicenceStatusDto(
                    State: "Valid",
                    LicenceId: valid.Licence.Id.Value,
                    Tier: valid.Licence.Tier,
                    Modules: valid.Licence.Modules,
                    Expiry: valid.Licence.Expiry,
                    RefusalReason: null,
                    Sentence: _displayNames.SentenceFor(status),
                    Advice: null,
                    StateDisplayName: _displayNames.StateNameFor("Valid"),
                    RefusalReasonDisplayName: null);

            case LicenceStatus.Absent:
                return new LicenceStatusDto(
                    State: "Absent",
                    LicenceId: null,
                    Tier: null,
                    Modules: null,
                    Expiry: null,
                    RefusalReason: null,
                    Sentence: _displayNames.SentenceFor(status),
                    Advice: null,
                    StateDisplayName: _displayNames.StateNameFor("Absent"),
                    RefusalReasonDisplayName: null);

            case LicenceStatus.Refused refused:
                return new LicenceStatusDto(
                    State: "Refused",
                    LicenceId: null,
                    Tier: null,
                    Modules: null,
                    Expiry: null,
                    RefusalReason: refused.Reason.ToString(),
                    Sentence: _displayNames.SentenceFor(status),
                    Advice: _displayNames.AdviceFor(refused.Reason),
                    StateDisplayName: _displayNames.StateNameFor("Refused"),
                    RefusalReasonDisplayName: _displayNames.ReasonNameFor(refused.Reason.ToString()));

            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown LicenceStatus case.");
        }
    }
}
