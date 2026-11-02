namespace Maran.Sdk.Contracts;

/// <summary>
/// The comparison could not be performed at all: the hash list was missing, unreadable, failed its
/// signature check, or (at build time) tripped the release build's vacuity floor. This is the case
/// that must never be reported as, or silently treated as, clean — an unanswered question is not a
/// clean answer.
/// </summary>
/// <param name="InstalledVersion">The release version the comparison believes is installed.</param>
/// <param name="Reason">
/// Which specific reason it could not run (missing hash list, unreadable, signature failure, vacuity
/// floor tripped) is the closed PluginLoader's own concern; this repository only carries the string
/// forward for the operator's log and the mail body, never for a customer-facing accusation.
/// </param>
/// <param name="ObservedAt">When the closed PluginLoader attempted this comparison, by its own clock.</param>
public sealed record UnavailableCodeIntegrityVerdict(string InstalledVersion, string Reason, DateTimeOffset ObservedAt)
    : CodeIntegrityVerdict(InstalledVersion, ObservedAt);
