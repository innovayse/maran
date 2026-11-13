using Maran.Modules.Licensing.Domain.Enums;

namespace Maran.Modules.Licensing.Common;

/// <summary>
/// The wire shape of the currently installed licence's three-state status (spec §228), as an
/// administrator reads it.
/// </summary>
/// <param name="State">
/// <see cref="LicenceStateKind.Valid"/>, <see cref="LicenceStateKind.Absent"/> or
/// <see cref="LicenceStateKind.Refused"/> — the discriminator naming which of the other
/// fields is populated. Never a bare boolean: an operator with no licence and an operator with a
/// forged one need different remedies, and collapsing the two into one flag is the exact trap
/// <c>Domain.Enums.LicenceStatus</c>'s own remarks were written to prevent.
/// </param>
/// <param name="LicenceId">
/// The installed licence's own opaque identifier (spec §228's <c>id</c> field), present only when
/// <paramref name="State"/> is <see cref="LicenceStateKind.Valid"/>. Innovayse's own issued
/// identifier, not a secret and not derived from anything on this server.
/// </param>
/// <param name="Tier">
/// The plan tier the licence carries, present only when <paramref name="State"/> is
/// <see cref="LicenceStateKind.Valid"/>.
/// </param>
/// <param name="Modules">
/// The module ids the licence unlocks, present only when <paramref name="State"/> is
/// <see cref="LicenceStateKind.Valid"/>.
/// </param>
/// <param name="Expiry">
/// The instant the licence lapses, present only when <paramref name="State"/> is
/// <see cref="LicenceStateKind.Valid"/>.
/// </param>
/// <param name="RefusalReason">
/// The specific, actionable reason (<see cref="Domain.Enums.LicenceRefusalReason"/>; see the remark on
/// its <c>FingerprintMismatch</c> member for why that one can never actually appear here), present
/// only when <paramref name="State"/> is <see cref="LicenceStateKind.Refused"/>.
/// </param>
/// <param name="Sentence">
/// The operator-facing sentence for whatever <paramref name="State"/> is, in the caller's own
/// language — the same text <c>Services.LicenceStatusDisplayNames.SentenceFor</c> logs at startup.
/// </param>
/// <param name="Advice">
/// What the operator can do about it, present only when <paramref name="State"/> is
/// <see cref="LicenceStateKind.Refused"/>.
/// </param>
/// <param name="StateDisplayName">
/// <paramref name="State"/>'s short, localized label (rules/architecture.md "The backend owns the
/// data, the SPA renders it") — resolved by <c>Services.LicenceStatusDisplayNames.StateNameFor</c>, the
/// same one <see cref="Sentence"/> already carries a longer form of. Kept alongside
/// <paramref name="State"/> rather than replacing it: scripts and support tickets still need the
/// machine value.
/// </param>
/// <param name="RefusalReasonDisplayName">
/// <paramref name="RefusalReason"/>'s short, localized label, present only when
/// <paramref name="RefusalReason"/> is — resolved by
/// <c>Services.LicenceStatusDisplayNames.ReasonNameFor</c>, which reads the same resx entry
/// <see cref="Advice"/>'s sibling sentence is paired with.
/// </param>
/// <param name="IsOffline">
/// Whether the licence is an offline licence, covered by its own expiry alone; false for every other
/// state. A signed fact of the licence.
/// </param>
/// <param name="Lease">
/// Where the licence's lease stands (<see cref="LeaseStandingKind"/>); <see cref="LeaseStandingKind.Missing"/>
/// when there is no valid licence to hold one and <see cref="LeaseStandingKind.NotRequired"/> for an offline
/// licence. The machine value, serialized by member name.
/// </param>
/// <param name="LeaseDisplayName">
/// <paramref name="Lease"/> as an operator reads it, in the caller's language. The lease's standing is
/// the only thing about the lease this response carries: not its text, signature or machine id.
/// </param>
/// <param name="LeaseValidUntil">When the lease stops (or stopped) being valid, when a trusted lease was read; else null.</param>
/// <param name="ServerId">
/// The machine id of this server exactly as the licence and lease comparisons read it, which is the value a
/// licence names in its <c>server</c> claim to be bound here. Present in every state, because an administrator
/// who has no licence yet needs it to claim one; null when the host has none or the agent could not be asked.
/// </param>
/// <param name="ClaimUrl">
/// The marketplace page where a licence is claimed for <paramref name="ServerId"/>, with the id in the address;
/// null when there is no id the marketplace accepts (32 hexadecimal digits) or no usable address for the page.
/// The panel never opens it: the administrator's browser does.
/// </param>
/// <param name="LeaseRequest">
/// What the request for a lease, made by the install this answer follows, amounted to. Present on the answer to an
/// install and null on a plain read, which asks nothing of the licence service.
/// </param>
/// <param name="LeaseRequestDisplayName">
/// <paramref name="LeaseRequest"/> as a sentence in the caller's language, saying what happened and whether the
/// panel will try again; null exactly when <paramref name="LeaseRequest"/> is.
/// </param>
/// <param name="ExpiryWarningDaysLeft">
/// Whole days until the licence expires (rounded up), present exactly when the licence is still in date and within one
/// of the configured warning thresholds (<c>Licensing:ExpiryWarningDays</c>, default 30, 14 and 3 days); null in every
/// other state, including an expired licence, which is a refusal of its own. The backend owns the thresholds.
/// </param>
/// <param name="ExpiryWarning">
/// <paramref name="ExpiryWarningDaysLeft"/> as a sentence in the caller's language, naming the expiry day; null exactly
/// when <paramref name="ExpiryWarningDaysLeft"/> is.
/// </param>
/// <param name="ExpiryRenewalAdvice">
/// The renewal steps in the caller's language (claim the licence again on the marketplace for this server, or install a
/// new licence file); null exactly when <paramref name="ExpiryWarningDaysLeft"/> is. The claim link itself is
/// <paramref name="ClaimUrl"/>.
/// </param>
/// <remarks>
/// <para>
/// <b>What this type may NOT carry, and why the fields above stop where they do.</b>
/// <c>docs/superpowers/notes/2026-09-22-licence-verification-threat-note.md</c> §4 draws the line for
/// the audit journal and it applies with the same force to a response an administrator reads over
/// HTTP: never the licence's raw signed bytes or its Ed25519 signature (that would hand a reader a
/// verbatim copy of the artefact Innovayse issued, and a known-good signature to study), and never the
/// primary interface's identifier, which the panel does not bind to. The machine id IS carried, as
/// <paramref name="ServerId"/> and nowhere else: it is the one fact an administrator needs in order to ask
/// for a licence bound to this server, it is readable by that administrator on the host already, and the
/// response is administrator-only. It still never reaches a log line or an audit entry
/// (<c>docs/superpowers/notes/2026-11-07-licensing-lease-threat-note.md</c>, fifth amendment). Nothing on
/// <see cref="Domain.Entities.Licence"/> or <see cref="Domain.Enums.LicenceRefusalReason"/> carries the
/// signature or the signed text, so this DTO cannot leak them by omission going stale.
/// </para>
/// <para>
/// <b>The licence id is judged safe to return.</b> Spec §228 states it as an opaque identifier
/// Innovayse's cabinet issued, not a secret and not a function of anything about this specific
/// server — the same judgement the threat note's audit-subject guidance makes for the same field.
/// </para>
/// </remarks>
public sealed record LicenceStatusDto(
    LicenceStateKind State,
    string? LicenceId,
    string? Tier,
    IReadOnlyList<string>? Modules,
    DateTimeOffset? Expiry,
    LicenceRefusalReason? RefusalReason,
    string Sentence,
    string? Advice,
    string StateDisplayName,
    string? RefusalReasonDisplayName,
    bool IsOffline,
    LeaseStandingKind Lease,
    string LeaseDisplayName,
    DateTimeOffset? LeaseValidUntil,
    string? ServerId,
    string? ClaimUrl,
    LeaseRenewalOutcome? LeaseRequest,
    string? LeaseRequestDisplayName,
    int? ExpiryWarningDaysLeft,
    string? ExpiryWarning,
    string? ExpiryRenewalAdvice);
