namespace Maran.Agent.Client.Services.DnsService;

/// <summary>
/// What the daemon was seen to do about signing one zone, read from the daemon's own configuration and signing
/// state and never from what the panel last sent. Each fact is its own three-valued answer: a daemon that could
/// not be asked certifies nothing, and an unknown wire value reads as not observed.
/// </summary>
/// <param name="Phase">The phase the daemon's configuration for the zone states; meaningful only when <paramref name="PhaseObserved"/> is in force.</param>
/// <param name="PhaseObserved">Whether <paramref name="Phase"/> was read from the daemon (in force) or could not be (not observed).</param>
/// <param name="PublishedKeyTag">The key tag of the key the daemon reports having signed the zone with; zero unless <paramref name="PublishedKeyObserved"/> is in force.</param>
/// <param name="PublishedKeyObserved">In force when the daemon reports a completed signing by <paramref name="PublishedKeyTag"/>, not in force when it reports none, not observed when it could not be asked.</param>
public sealed record DnsSigningObservedDto(
    DnsSigningPhase Phase,
    DnsPolicyState PhaseObserved,
    int PublishedKeyTag,
    DnsPolicyState PublishedKeyObserved);
