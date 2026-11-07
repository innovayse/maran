namespace Maran.Modules.Identity.Models;

/// <summary>What one retention run did to the audit journal.</summary>
/// <param name="Purged">How many rows the run deleted; zero when retention is off or nothing had aged out.</param>
/// <param name="ThroughChainIndex">
/// The highest chain index that has been purged so far, as the chain anchor records it after the run;
/// zero when nothing was ever purged. It is the anchor's value, not only this run's, so a run that
/// deleted nothing still states how far the journal's old end has been cut.
/// </param>
/// <param name="Cutoff">
/// The instant rows older than which were eligible, or <c>null</c> when retention is off (or its
/// setting was not trusted), which is the run's way of saying that nothing was eligible at all.
/// </param>
public sealed record AuditRetentionOutcome(int Purged, long ThroughChainIndex, DateTimeOffset? Cutoff);
