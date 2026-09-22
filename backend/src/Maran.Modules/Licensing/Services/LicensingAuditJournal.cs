using Maran.Modules.Licensing.Domain.Enums;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Writes this module's audit entries: today, exactly one — that the installation's licence status
/// was read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a read gets an entry at all, argued rather than assumed.</b> Reading the status is not a
/// mutation the way every other <c>*AuditJournal</c> in this panel exists to record, and this module
/// was asked to decide either way rather than add one reflexively. The case FOR: a licence status
/// names the product, the tier and the expiry of the WHOLE installation (see
/// <c>Controllers.LicensingController</c>'s own remarks on why the read is administrator-only for the
/// identical reason), so who looked at that fact and when is exactly the kind of question an operator
/// audit trail exists to answer — no different in kind from <c>AuditActions.SiteLogTailed</c>, which
/// this panel already journals on every read for the same reason: an installation-wide or otherwise
/// sensitive fact being read is itself an event worth a date and an author, not only its being
/// changed. The case AGAINST would be that a GET produces noise — but this GET is administrator-only,
/// has no polling caller in this slice, and its one journalled fact (which of three states, and
/// which reason) is cheap to write and is exactly what the threat note's own §4 already worked out the
/// safe shape of. That balance is why this entry exists.
/// </para>
/// <para>
/// <b>The subject is <c>key=value;key=value</c>, never prose, and never localized</b> — the same rule
/// <c>Databases.Services.DatabaseAuditJournal</c>'s sibling entries follow, restated here because a
/// prose subject shipped in an audit entry in this exact codebase days before this one was written and
/// was found on a Russian screen. <see cref="RecordReadAsync"/> builds it from
/// <see cref="Domain.Enums.LicenceStatus"/> alone, in English key names and enum member spellings that
/// do not change with the reader's locale.
/// </para>
/// <para>
/// <b>What the subject may NEVER carry</b>, per the threat note's own §4: the licence's raw signed
/// bytes, its Ed25519 signature, or the raw fingerprint inputs (<c>machine-id</c>, the primary
/// interface). None of those are reachable from a <see cref="LicenceStatus"/> value in the first
/// place — <see cref="Domain.Entities.Licence"/> carries none of them — so this type cannot leak them
/// by construction, not merely by care.
/// </para>
/// <para>
/// <b>Always <see cref="RecordSuccessAsync"/>, never a failure entry.</b> Unlike every other journal in
/// this panel, this one has no failure half to record: <see cref="Services.LicenceVerifier"/> never
/// throws and never reports anything the handler could translate into a refused write (spec §229 —
/// see <c>Queries.GetLicenceStatus.GetLicenceStatusQueryHandler</c>'s own remarks), so
/// <see cref="RecordFailureAsync"/> exists only for symmetry with the module's Result-returning
/// convention and is asserted, by this module's own tests, to never actually be called.
/// </para>
/// </remarks>
public sealed class LicensingAuditJournal
{
    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public LicensingAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records that the installed licence's status was read, and what it was.</summary>
    /// <param name="status">The three-state outcome the verifier just produced.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// Always a success entry: see this type's own remarks for why <see cref="RecordFailureAsync"/> is
    /// never reached from here. <c>Refused</c> is a fact this read reported correctly, not a failure of
    /// the READ itself — the same distinction <see cref="Domain.Enums.LicenceStatus"/> exists to draw
    /// between the verifier's own outcome and whether the caller's request succeeded.
    /// </remarks>
    public async Task RecordReadAsync(
        LicenceStatus status,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await RecordSuccessAsync(AuditActions.LicenceStatusRead, SubjectFor(status), ipAddress, userAgent, cancellationToken);
    }

    /// <summary>Records an operation that took effect.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">The machine-readable, never-localized <c>key=value;key=value</c> subject.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordSuccessAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(action, subject, ipAddress, userAgent, succeeded: true, cancellationToken);
    }

    /// <summary>
    /// Records an operation that was refused. This module's <c>AuditActions.LicenceStatusRead</c> entry
    /// never reaches this method — a read never fails, see this type's own remarks — but
    /// <c>AuditActions.LicenceInstalled</c> does, from <c>Commands.InstallLicence.InstallLicenceCommandHandler</c>.
    /// </summary>
    /// <param name="action">The action that was attempted, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordFailureAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(action, subject, ipAddress, userAgent, succeeded: false, cancellationToken);
    }

    /// <summary>Builds the machine-readable subject for one status, per this type's own carry/never-carry rules.</summary>
    /// <param name="status">The status just observed.</param>
    /// <returns>
    /// <c>status=Valid;id=...;tier=...</c> for a valid licence, <c>status=Absent</c> for none installed,
    /// or <c>status=Refused;reason=...</c> for a refusal — never the licence's signature or fingerprint.
    /// </returns>
    private static string SubjectFor(LicenceStatus status)
    {
        return status switch
        {
            LicenceStatus.Valid valid =>
                $"status=Valid;id={valid.Licence.Id.Value};tier={valid.Licence.Tier}",
            LicenceStatus.Absent => "status=Absent",
            LicenceStatus.Refused refused => $"status=Refused;reason={refused.Reason}",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown LicenceStatus case."),
        };
    }

    /// <summary>Builds and stores one entry.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">The machine-readable subject.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether the operation took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    private async Task WriteAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(
            new AuditEntry(
                _currentUser.UserId,
                _currentUser.Username,
                action,
                subject,
                ipAddress,
                userAgent,
                succeeded),
            cancellationToken);
    }
}
