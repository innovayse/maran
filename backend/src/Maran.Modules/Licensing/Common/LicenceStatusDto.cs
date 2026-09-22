namespace Maran.Modules.Licensing.Common;

/// <summary>
/// The wire shape of the currently installed licence's three-state status (spec §228), as an
/// administrator reads it.
/// </summary>
/// <param name="State">
/// <c>"Valid"</c>, <c>"Absent"</c> or <c>"Refused"</c> — the discriminator naming which of the other
/// fields is populated. Never a bare boolean: an operator with no licence and an operator with a
/// forged one need different remedies, and collapsing the two into one flag is the exact trap
/// <c>Domain.Enums.LicenceStatus</c>'s own remarks were written to prevent.
/// </param>
/// <param name="LicenceId">
/// The installed licence's own opaque identifier (spec §228's <c>id</c> field), present only when
/// <paramref name="State"/> is <c>"Valid"</c>. Innovayse's own issued identifier, not a secret and not
/// derived from anything on this server.
/// </param>
/// <param name="Tier">The plan tier the licence carries, present only when <paramref name="State"/> is <c>"Valid"</c>.</param>
/// <param name="Modules">The module ids the licence unlocks, present only when <paramref name="State"/> is <c>"Valid"</c>.</param>
/// <param name="Expiry">The instant the licence lapses, present only when <paramref name="State"/> is <c>"Valid"</c>.</param>
/// <param name="RefusalReason">
/// The specific, actionable reason (<c>Malformed</c>, <c>SignatureInvalid</c>, <c>Expired</c>,
/// <c>ProductMismatch</c>, or <c>FingerprintMismatch</c> — see the remark on that last member for why
/// it can never actually appear here), present only when <paramref name="State"/> is <c>"Refused"</c>.
/// </param>
/// <param name="Sentence">
/// The operator-facing sentence for whatever <paramref name="State"/> is, in the caller's own
/// language — the same text <c>Services.LicenceStatusDisplayNames.SentenceFor</c> logs at startup.
/// </param>
/// <param name="Advice">
/// What the operator can do about it, present only when <paramref name="State"/> is <c>"Refused"</c>.
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
/// <remarks>
/// <para>
/// <b>What this type may NOT carry, and why the fields above stop where they do.</b>
/// <c>docs/superpowers/notes/2026-09-22-licence-verification-threat-note.md</c> §4 draws the line for
/// the audit journal and it applies with the same force to a response an administrator reads over
/// HTTP: never the licence's raw signed bytes or its Ed25519 signature (that would hand a reader a
/// verbatim copy of the artefact Innovayse issued, and a known-good signature to study), and never the
/// raw fingerprint inputs — <c>machine-id</c> and the primary interface's identifier — because
/// §228's fingerprinting is not implemented in this tree at all (see the threat note §5) and, even
/// once it is, <c>ServerFingerprint</c>'s own design note says only its equality-check OUTCOME may
/// ever leave that type. Nothing on <see cref="Domain.Entities.Licence"/> or
/// <see cref="Domain.Enums.LicenceRefusalReason"/> carries either of those today, so this DTO cannot
/// leak them by omission going stale — there is nothing on the source types to have forgotten to
/// exclude.
/// </para>
/// <para>
/// <b>The licence id is judged safe to return.</b> Spec §228 states it as an opaque identifier
/// Innovayse's cabinet issued, not a secret and not a function of anything about this specific
/// server — the same judgement the threat note's audit-subject guidance makes for the same field.
/// </para>
/// </remarks>
public sealed record LicenceStatusDto(
    string State,
    string? LicenceId,
    string? Tier,
    IReadOnlyList<string>? Modules,
    DateTimeOffset? Expiry,
    string? RefusalReason,
    string Sentence,
    string? Advice,
    string StateDisplayName,
    string? RefusalReasonDisplayName);
