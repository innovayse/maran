namespace Maran.Sdk.Contracts;

/// <summary>
/// What the closed PluginLoader's comparison of the installed files against the release's signed
/// hash list found, the last time it checked.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never a <c>bool</c>.</b> "The files match" and "nothing could be compared" are two different
/// facts a caller must not be able to collapse into one by accident — the same reason
/// <c>IAccountResidueAuditor.AccountResidue.Unchecked</c> is a case of its own rather than treated
/// as "found nothing" (backend/src/Maran.Sdk/Interfaces/IAccountResidueAuditor.cs). A verifier that
/// cannot read its own hash list and reports <see cref="Clean"/> for want of a third option is the
/// single most dangerous failure mode this feature has: it is the outcome that looks healthiest
/// while telling an operator nothing was ever compared.
/// </para>
/// <para>
/// <b>A closed set, per docs/superpowers/plans/2026-09-19-maran-code-integrity.md Task 2.</b> Adding
/// a fourth outcome later is a decision for whoever next reads that plan, not something a caller of
/// this enum should be able to invent by combining these three.
/// </para>
/// </remarks>
public enum CodeIntegrityOutcome
{
    /// <summary>
    /// Every file the comparison could name matched the signed hash list for the version reported in
    /// the same <c>CodeIntegrityReport</c>. This is a statement of fact about the files the release
    /// shipped — it says nothing about files a customer added, which this mechanism cannot see (see
    /// <see cref="CodeIntegrityReport"/>'s own remarks).
    /// </summary>
    Clean = 0,

    /// <summary>
    /// At least one file the hash list names does not match what is installed. This is reported as a
    /// FACT — "these files differ from release X" — never as an accusation: BSL explicitly permits a
    /// customer to modify their own installation, and a customer exercising that right must never
    /// read this outcome as a claim that they are running compromised or pirated software.
    /// </summary>
    Drifted = 1,

    /// <summary>
    /// The comparison could not be performed at all: the hash list was missing, unreadable, failed
    /// its signature check, or (at build time) tripped the release build's vacuity floor. This is the
    /// outcome that must never be reported as, or silently treated as, <see cref="Clean"/> — an
    /// unanswered question is not a clean answer.
    /// </summary>
    Unavailable = 2,
}
