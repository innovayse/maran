namespace Maran.Sdk.Contracts;

/// <summary>
/// One finding from the closed PluginLoader's comparison of the panel's installed files against the
/// signed hash list for the release version it believes is running.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is a REPORT OF FACT, never a verdict.</b> Per
/// docs/superpowers/specs/2026-09-19-maran-code-integrity.md §2 and §5: BSL explicitly permits a
/// customer to modify their own installation, and a hash list plus a verifier that both run on a
/// server the customer (or an attacker who has taken it over) fully controls is a deterrent, not a
/// proof. Nothing on this type, and nothing built from it, may imply otherwise — no property here is
/// named or documented as "tampered", "compromised", or "pirated", and neither should any string a
/// caller builds from <see cref="DifferingPaths"/>.
/// </para>
/// <para>
/// <b>What it cannot see.</b> The hash list this report is compared against enumerates only files a
/// release actually shipped (docs/superpowers/specs/2026-09-19-maran-code-integrity.md §4) — never a
/// live filesystem walk. A file a customer or an attacker ADDS anywhere on the host is therefore
/// invisible to this report: it answers "does the shipped code match", never "is anything extra
/// present." A <see cref="CodeIntegrityOutcome.Clean"/> report is not evidence that nothing extra was
/// planted.
/// </para>
/// </remarks>
/// <param name="Outcome">
/// What the comparison found. See <see cref="CodeIntegrityOutcome"/> for why this is never a
/// <c>bool</c>.
/// </param>
/// <param name="InstalledVersion">
/// The release version the comparison believes is currently installed — the anchor that keeps an
/// ordinary update from producing noise, per the spec addendum §2: a hash list not pinned to the
/// version actually running would flag every legitimate patch.
/// </param>
/// <param name="DifferingPaths">
/// The paths (relative to the install root, matching the hash list's own keys) that did not match.
/// Empty unless <paramref name="Outcome"/> is <see cref="CodeIntegrityOutcome.Drifted"/>; a caller
/// that receives <see cref="CodeIntegrityOutcome.Drifted"/> with an empty list has been handed an
/// internally inconsistent report and must not interpret it charitably as clean.
/// </param>
/// <param name="UnavailableReason">
/// Non-null only when <paramref name="Outcome"/> is <see cref="CodeIntegrityOutcome.Unavailable"/> —
/// which specific reason (missing hash list, unreadable, signature failure, vacuity floor tripped)
/// is the closed PluginLoader's own concern; this repository only carries the string forward for the
/// operator's log and the mail body, never for a customer-facing accusation.
/// </param>
/// <param name="ObservedAt">When the closed PluginLoader performed this comparison, by its own clock.</param>
public sealed record CodeIntegrityReport(
    CodeIntegrityOutcome Outcome,
    string InstalledVersion,
    IReadOnlyList<string> DifferingPaths,
    string? UnavailableReason,
    DateTimeOffset ObservedAt);
