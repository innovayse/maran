namespace Maran.Sdk.Contracts;

/// <summary>
/// One finding from the closed PluginLoader's comparison of the panel's installed files against the
/// signed hash list for the release version it believes is running. Exactly one of three cases, each
/// a type of its own so a case carries only what it has: <see cref="CleanCodeIntegrityReport"/>,
/// <see cref="DriftedCodeIntegrityReport"/> (the differing paths) and
/// <see cref="UnavailableCodeIntegrityReport"/> (the reason nothing could be compared).
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is a REPORT OF FACT, never a verdict.</b> Per
/// docs/superpowers/specs/2026-09-19-maran-code-integrity.md §2 and §5: BSL explicitly permits a
/// customer to modify their own installation, and a hash list plus a verifier that both run on a
/// server the customer (or an attacker who has taken it over) fully controls is a deterrent, not a
/// proof. Nothing on this type, and nothing built from it, may imply otherwise — no property here is
/// named or documented as "tampered", "compromised", or "pirated", and neither should any string a
/// caller builds from <see cref="DriftedCodeIntegrityReport.DifferingPaths"/>.
/// </para>
/// <para>
/// <b>What it cannot see.</b> The hash list this report is compared against enumerates only files a
/// release actually shipped (docs/superpowers/specs/2026-09-19-maran-code-integrity.md §4) — never a
/// live filesystem walk. A file a customer or an attacker ADDS anywhere on the host is therefore
/// invisible to this report: it answers "does the shipped code match", never "is anything extra
/// present." A <see cref="CleanCodeIntegrityReport"/> is not evidence that nothing extra was
/// planted.
/// </para>
/// <para>
/// <b>Never a <c>bool</c>, and never collapsed.</b> "The files match" and "nothing could be compared"
/// are two different facts a caller must not be able to collapse into one by accident — the same
/// reason <c>IAccountResidueAuditor.AccountResidue.Unchecked</c> is a case of its own rather than
/// treated as "found nothing". A verifier that cannot read its own hash list and reports clean for
/// want of a third option is the single most dangerous failure mode this feature has: it is the
/// answer that looks healthiest while telling an operator nothing was ever compared. The set is
/// closed on purpose (docs/superpowers/plans/2026-09-19-maran-code-integrity.md Task 2): the
/// three cases are the whole set, and a receiver treats any other derived type as a report it cannot
/// interpret rather than as a clean one.
/// </para>
/// </remarks>
/// <param name="InstalledVersion">
/// The release version the comparison believes is currently installed — the anchor that keeps an
/// ordinary update from producing noise, per the spec addendum §2: a hash list not pinned to the
/// version actually running would flag every legitimate patch.
/// </param>
/// <param name="ObservedAt">When the closed PluginLoader performed this comparison, by its own clock.</param>
public abstract record CodeIntegrityReport(string InstalledVersion, DateTimeOffset ObservedAt);
