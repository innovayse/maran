using Maran.Modules.Licensing.Common;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Resources;
using Maran.Modules.Licensing.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Commands.InstallLicence;

/// <summary>
/// Handles <see cref="InstallLicenceCommand"/>: verifies the uploaded licence text and, only on
/// <see cref="LicenceStatus.Valid"/>, installs it as the new licence artefact — spec §228's write
/// half, exactly as
/// <c>docs/superpowers/notes/2026-09-22-licence-installation-threat-note.md</c> settled it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Verify first, persist only on <c>Valid</c> — one uninterruptible sequence, per the threat
/// note's §3 and §7 item 1.</b> <see cref="HandleAsync"/> calls
/// <see cref="Services.LicenceVerifier.VerifyAsync"/> on the uploaded bytes directly — the SAME
/// verifier, the same three-state result the read-only status route uses — and
/// <see cref="Interfaces.ILicenceWriter.InstallAsync"/> is reached from exactly one branch: the
/// <c>switch</c> arm for <see cref="LicenceStatus.Valid"/>. There is no other code path from a
/// verification result to a write; a future edit that reorders these two steps would have to change
/// this method's own control flow to do it, not merely skip a guard.
/// </para>
/// <para>
/// <b>Serialized by <see cref="Services.LicenceInstallLock"/>.</b> The whole verify-then-write
/// sequence for one request runs under the module's single in-process gate, so two concurrent
/// installs can never race two temp-file writes against the one <c>rename()</c> target (threat
/// note §4's last bullet).
/// </para>
/// <para>
/// <b>Each rejection is its own distinguishable error code</b> — never one generic "invalid
/// licence": <see cref="LicenceRefusalReason"/>'s five members map onto
/// <c>LicenceInstallMalformed</c>, <c>LicenceInstallSignatureInvalid</c>, <c>LicenceInstallExpired</c>,
/// <c>LicenceInstallProductMismatch</c> and <c>LicenceInstallFingerprintMismatch</c> in
/// <c>Resources/ErrorMessages.resx</c>, each classified <see cref="ErrorType.Validation"/> in
/// <c>ExpectedErrorStatuses</c> — the caller uploaded something they could have sent differently,
/// never a server fault. On any rejection, <see cref="Interfaces.ILicenceWriter"/> is never called at
/// all, so the previous licence artefact, if any, is left byte-for-byte as it was.
/// </para>
/// <para>
/// <b>Installing a licence does NOT bind it to this server</b> — the single most important sentence
/// in the threat note (§6). <see cref="LicenceRefusalReason.FingerprintMismatch"/> is unreachable:
/// nothing in this tree computes a server fingerprint, so a licence that verifies <c>Valid</c> here
/// verifies <c>Valid</c>, byte-for-byte, on any other Maran installation carrying the same public key.
/// This handler adds no fingerprint check it does not have the means to perform, and the honesty
/// obligation is carried on the wire by <see cref="LicenceStatusDto"/>'s own sentence text rather than
/// silently implied away.
/// </para>
/// <para>
/// <b>What the response and the audit entry never carry</b> — threat note §5: the raw signed bytes
/// and the Ed25519 signature are never echoed back and never journalled; on success this handler
/// returns the same <see cref="LicenceStatusDto"/> shape the read-only status route already returns —
/// id, tier, expiry — never a copy of the request body.
/// </para>
/// <para>
/// <b>The audit entry.</b> <see cref="AuditActions.LicenceInstalled"/> is written on EVERY outcome,
/// success and refusal alike — an installation attempt against the whole panel's paid-module gate is
/// worth a date and an author regardless of whether it succeeded, the same argument
/// <c>Services.LicensingAuditJournal</c> already makes for a mere READ. The subject is
/// <c>key=value;key=value</c>, carrying the outcome and, on success, the new licence's id/tier and the
/// PREVIOUS licence's id when one existed (a licence id is explicitly not a secret — see
/// <c>Domain.ValueObjects.LicenceId</c>'s own remarks) — never the uploaded bytes, signature, or
/// fingerprint inputs.
/// </para>
/// </remarks>
public sealed class InstallLicenceCommandHandler
{
    /// <summary>Where the currently installed licence's raw text, if any, is read from.</summary>
    private readonly ILicenceRawTextSource _rawTextSource;

    /// <summary>This module's offline verifier — the same one the read-only status route uses.</summary>
    private readonly LicenceVerifier _verifier;

    /// <summary>Installs the new artefact atomically, once verification has already returned <c>Valid</c>.</summary>
    private readonly ILicenceWriter _writer;

    /// <summary>Serializes this handler's verify-then-write sequence across concurrent requests.</summary>
    private readonly LicenceInstallLock _installLock;

    /// <summary>Resolves the operator-facing sentence for the newly installed status.</summary>
    private readonly LicenceStatusDisplayNames _displayNames;

    /// <summary>Writes this module's audit entries.</summary>
    private readonly LicensingAuditJournal _auditJournal;

    /// <summary>Creates the handler.</summary>
    /// <param name="rawTextSource">Where the currently installed licence's raw text, if any, is read from.</param>
    /// <param name="verifier">This module's offline verifier.</param>
    /// <param name="writer">Installs the new artefact atomically.</param>
    /// <param name="installLock">Serializes the verify-then-write sequence.</param>
    /// <param name="displayNames">Resolves the operator-facing sentence for the newly installed status.</param>
    /// <param name="auditJournal">Writes this module's audit entries.</param>
    public InstallLicenceCommandHandler(
        ILicenceRawTextSource rawTextSource,
        LicenceVerifier verifier,
        ILicenceWriter writer,
        LicenceInstallLock installLock,
        LicenceStatusDisplayNames displayNames,
        LicensingAuditJournal auditJournal)
    {
        _rawTextSource = rawTextSource;
        _verifier = verifier;
        _writer = writer;
        _installLock = installLock;
        _displayNames = displayNames;
        _auditJournal = auditJournal;
    }

    /// <summary>Verifies the uploaded licence text and installs it only when it verifies <c>Valid</c>.</summary>
    /// <param name="command">The uploaded licence text, and who is asking.</param>
    /// <param name="cancellationToken">Cancellation token for verification, the write, and the audit write.</param>
    /// <returns>
    /// The newly installed licence's status on success; one of the five <c>LicenceInstall*</c> codes,
    /// typed <see cref="ErrorType.Validation"/>, on a refusal.
    /// </returns>
    public async Task<Result<LicenceStatusDto>> HandleAsync(
        InstallLicenceCommand command,
        CancellationToken cancellationToken)
    {
        return await _installLock.RunExclusiveAsync(
            token =>
            {
                return VerifyThenInstallAsync(command, token);
            },
            cancellationToken);
    }

    /// <summary>
    /// The uninterruptible sequence itself, run under <see cref="LicenceInstallLock"/>: verify, and
    /// only on <c>Valid</c>, read the previous licence's id and install the new one.
    /// </summary>
    /// <param name="command">The uploaded licence text, and who is asking.</param>
    /// <param name="cancellationToken">Cancellation token for the sequence.</param>
    private async Task<Result<LicenceStatusDto>> VerifyThenInstallAsync(
        InstallLicenceCommand command,
        CancellationToken cancellationToken)
    {
        var status = await _verifier.VerifyAsync(command.RawLicenceText, cancellationToken);

        if (status is not LicenceStatus.Valid valid)
        {
            var reason = ReasonOf(status);
            await RecordAsync(command, succeeded: false, SubjectForRefusal(reason), cancellationToken);

            return Result<LicenceStatusDto>.Fail(Error.Of(CodeFor(reason), ErrorType.Validation));
        }

        // Read the PREVIOUS licence's id, if any, before it is replaced — safe to journal (LicenceId's
        // own remarks), and the only way an operator reading the trail later can reconstruct "licence
        // A was replaced by licence B."
        var previousId = PreviousLicenceIdOrNull(cancellationToken);

        // The one call this whole sequence exists to gate: never reached for anything but Valid.
        try
        {
            await _writer.InstallAsync(command.RawLicenceText, cancellationToken);
        }
        catch (Exception)
        {
            // MEASURED GAP, now closed. A write that throws — the licence directory not writable is
            // the ordinary cause, and it is exactly what a dev or a misconfigured unit hits — used to
            // leave NO entry at all: the exception went straight past the journal to the middleware,
            // the caller got a 500, and the trail showed nothing between "status read: Absent" and
            // the operator's confusion. Verification had SUCCEEDED by this point, so the one thing
            // the journal must never lose is precisely this: somebody presented a good licence and
            // the panel failed to keep it.
            //
            // The entry is written and the exception RETHROWN, deliberately. Swallowing it would
            // report an install that did not happen, which is the worse of the two failures: the
            // operator would believe the licence is in place.
            await RecordAsync(command, succeeded: false, SubjectForWriteFailure(), cancellationToken);

            throw;
        }

        await RecordAsync(
            command,
            succeeded: true,
            SubjectForSuccess(valid, previousId),
            cancellationToken);

        return Result<LicenceStatusDto>.Ok(ToDto(valid));
    }

    /// <summary>Re-verifies whatever was installed BEFORE this write, so its id can be journalled if valid.</summary>
    /// <param name="cancellationToken">Cancellation token for the verification.</param>
    /// <returns>The previous licence's id, or <c>null</c> when nothing was installed or it did not verify.</returns>
    private string? PreviousLicenceIdOrNull(CancellationToken cancellationToken)
    {
        var previousRawText = _rawTextSource.ReadRawLicenceText();
        var previousStatus = _verifier.VerifyAsync(previousRawText, cancellationToken).GetAwaiter().GetResult();

        return previousStatus is LicenceStatus.Valid previousValid ? previousValid.Licence.Id.Value : null;
    }

    /// <summary>Maps a non-<c>Valid</c> status to its refusal reason.</summary>
    /// <param name="status">The status the verifier returned; never <see cref="LicenceStatus.Valid"/> here.</param>
    private static LicenceRefusalReason ReasonOf(LicenceStatus status)
    {
        return status switch
        {
            LicenceStatus.Refused refused => refused.Reason,
            // Absent has no refusal reason of its own (see LicenceStatus's remarks) — installing
            // empty/whitespace text cannot reach here because the validator already refuses it, so
            // the only way Absent arrives is defensive: report it the same as an unreadable envelope.
            LicenceStatus.Absent => LicenceRefusalReason.Malformed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown LicenceStatus case."),
        };
    }

    /// <summary>Maps a refusal reason to its machine-stable, distinguishable error code.</summary>
    /// <param name="reason">The specific reason the licence was refused.</param>
    private static string CodeFor(LicenceRefusalReason reason)
    {
        return reason switch
        {
            LicenceRefusalReason.Malformed => nameof(ErrorMessages.LicenceInstallMalformed),
            LicenceRefusalReason.SignatureInvalid => nameof(ErrorMessages.LicenceInstallSignatureInvalid),
            LicenceRefusalReason.Expired => nameof(ErrorMessages.LicenceInstallExpired),
            LicenceRefusalReason.ProductMismatch => nameof(ErrorMessages.LicenceInstallProductMismatch),
            LicenceRefusalReason.FingerprintMismatch => nameof(ErrorMessages.LicenceInstallFingerprintMismatch),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown LicenceRefusalReason."),
        };
    }

    /// <summary>Builds the machine-readable subject for a refusal. Never the uploaded bytes — threat note §5.</summary>
    /// <param name="reason">The specific reason the licence was refused.</param>
    private static string SubjectForRefusal(LicenceRefusalReason reason)
    {
        return $"action=Install;status=Refused;reason={reason}";
    }

    /// <summary>Builds the machine-readable subject for a successful install.</summary>
    /// <param name="valid">The newly verified, now-installed licence.</param>
    /// <param name="previousLicenceId">The previous licence's id, or <c>null</c> when none existed.</param>
    private static string SubjectForSuccess(LicenceStatus.Valid valid, string? previousLicenceId)
    {
        var subject = $"action=Install;status=Valid;id={valid.Licence.Id.Value};tier={valid.Licence.Tier}";

        return previousLicenceId is null ? subject : subject + $";previousId={previousLicenceId}";
    }

    /// <summary>The audit subject for a licence that verified but could not be written.</summary>
    /// <returns>The machine-readable subject.</returns>
    /// <remarks>
    /// It names the STAGE rather than the exception: the cause belongs in the error log with its
    /// stack, and an audit subject carrying a filesystem path or an exception message would put
    /// host detail into a journal that is shown in a browser. "Verified, then not persisted" is the
    /// whole fact an operator needs from this row — the licence was good and the panel did not keep
    /// it, so nothing changed and the previous licence, if any, still stands.
    /// </remarks>
    private static string SubjectForWriteFailure()
    {
        return "action=Install;status=Valid;persisted=false";
    }

    /// <summary>Writes this handler's one audit entry, for either outcome.</summary>
    /// <param name="command">The caller, for the address and user agent the entry is stamped with.</param>
    /// <param name="succeeded">Whether the install took effect.</param>
    /// <param name="subject">The machine-readable subject — never the uploaded bytes, signature, or fingerprint inputs.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    private async Task RecordAsync(
        InstallLicenceCommand command,
        bool succeeded,
        string subject,
        CancellationToken cancellationToken)
    {
        if (succeeded)
        {
            await _auditJournal.RecordSuccessAsync(
                AuditActions.LicenceInstalled,
                subject,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return;
        }

        await _auditJournal.RecordFailureAsync(
            AuditActions.LicenceInstalled,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);
    }

    /// <summary>Maps the newly installed, verified licence to the same wire shape the read-only status route returns.</summary>
    /// <param name="valid">The newly verified licence.</param>
    /// <returns>
    /// The DTO — never a copy of the uploaded request body (threat note §5). Its sentence also says
    /// what decides whether the licence is tied to this server: the licence's own `server` claim,
    /// which a licence naming no server simply does not carry.
    /// </returns>
    private LicenceStatusDto ToDto(LicenceStatus.Valid valid)
    {
        return new LicenceStatusDto(
            State: "Valid",
            LicenceId: valid.Licence.Id.Value,
            Tier: valid.Licence.Tier,
            Modules: valid.Licence.Modules,
            Expiry: valid.Licence.Expiry,
            RefusalReason: null,
            // Not SentenceFor(valid): the install response needs its own sentence, one that states
            // the threat note's §6 fact outright — installing a licence does NOT bind it to this
            // server — rather than the read-only route's plain "installed and verified", which an
            // operator who just uploaded a file would read as reassurance it does not warrant.
            Sentence: _displayNames.InstalledSentence(),
            Advice: null,
            StateDisplayName: _displayNames.StateNameFor("Valid"),
            RefusalReasonDisplayName: null);
    }
}
