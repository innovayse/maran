using Maran.Modules.Firewall.Domain.Enums;

namespace Maran.Modules.Firewall.Domain.Entities;

/// <summary>
/// One occasion on which an address was banned from the host: who decided, why, and when the ban
/// runs out (spec §15).
/// </summary>
/// <remarks>
/// <para>
/// <b>This row is the durable store, and there is no other.</b> Both supported families' nftables
/// units flush the ruleset on stop and on reload, and the agent deliberately keeps no ban state of
/// its own — a ban is a set element in a kernel table that a reboot, a <c>systemctl reload</c> or an
/// unrelated ruleset change takes with it. So a ban that outlives a restart does so because
/// <c>StartupBanReconciler</c> read these rows and asked for it again; without them, every ban the
/// panel has ever placed is silently gone the next time the machine comes up.
/// </para>
/// <para>
/// <b>The reason is here because it can be nowhere else.</b> Nothing on the wire carries it: the
/// agent stores none and would have to put it in an nftables comment, whose argument <c>nft</c>
/// parses in its own grammar. A ban read back from the kernel is an address and a countdown.
/// </para>
/// <para>
/// <b>An expired or lifted episode is kept, not deleted.</b> The escalation ladder counts how often
/// one address has been banned inside the last day, so the rows that are no longer in force are
/// exactly the ones that decide how long the next ban lasts.
/// </para>
/// </remarks>
public sealed class BanEpisode
{
    /// <summary>
    /// The shortest ban the contract can express, and therefore the shortest remainder worth
    /// re-applying.
    /// </summary>
    /// <remarks>
    /// The wire carries whole seconds and 0 there means "permanent until somebody unbans it", so a
    /// remainder of half a second cannot be sent as itself — it would arrive as a ban nobody can
    /// wait out. An episode with less than this left to run is treated as finished, which is what it
    /// is about to be anyway.
    /// </remarks>
    private static readonly TimeSpan ShortestExpressibleRemainder = TimeSpan.FromSeconds(1);

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The banned address, in the form the agent accepts: plain IPv4 or plain IPv6, never the
    /// IPv4-mapped IPv6 spelling a dual-stack listener reports.
    /// </summary>
    /// <remarks>
    /// Normalised by <c>IpAddressNormalizer</c> before it ever reaches this constructor. An address
    /// stored as <c>::ffff:203.0.113.7</c> would be refused by the agent on every reconciliation
    /// pass, so the row would describe a ban that has never existed.
    /// </remarks>
    public string IpAddress { get; private set; }

    /// <summary>Why the ban was placed. Recorded here because the agent records none.</summary>
    public BanReason Reason { get; private set; }

    /// <summary>
    /// The start of the detection window this episode answers, or <c>null</c> for a ban an
    /// administrator asked for.
    /// </summary>
    /// <remarks>
    /// The idempotency key, paired with <see cref="IpAddress"/>. A detector's message can be
    /// delivered more than once — that is the durable queue behaving correctly — and a second
    /// delivery must not extend the ban or count as a second offence towards the escalation ladder.
    /// It is null for a manual ban because an administrator asking twice IS two decisions, and the
    /// database's unique index treats nulls as distinct, which is exactly that rule.
    /// </remarks>
    public DateTimeOffset? WindowStart { get; private set; }

    /// <summary>
    /// How many failures the detector counted in that window; zero for a manual ban.
    /// </summary>
    public int Failures { get; private set; }

    /// <summary>When the ban was placed.</summary>
    public DateTimeOffset BannedAt { get; private set; }

    /// <summary>When the ban runs out, or <c>null</c> for one that lasts until somebody lifts it.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>When the ban was lifted early, or <c>null</c> while it has not been.</summary>
    public DateTimeOffset? LiftedAt { get; private set; }

    /// <summary>Records a ban the agent has already installed.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="ipAddress">The banned address, already normalised to the form the agent accepts.</param>
    /// <param name="reason">Why the ban was placed.</param>
    /// <param name="windowStart">The detection window this episode answers, or null for a manual ban.</param>
    /// <param name="failures">How many failures the detector counted; zero for a manual ban.</param>
    /// <param name="bannedAt">When the ban was placed, taken from <see cref="IClock"/>.</param>
    /// <param name="expiresAt">When it runs out, or null for a ban that lasts until it is lifted.</param>
    public BanEpisode(
        Guid id,
        string ipAddress,
        BanReason reason,
        DateTimeOffset? windowStart,
        int failures,
        DateTimeOffset bannedAt,
        DateTimeOffset? expiresAt)
    {
        Id = id;
        IpAddress = ipAddress;
        Reason = reason;
        WindowStart = windowStart;
        Failures = failures;
        BannedAt = bannedAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private BanEpisode()
    {
        IpAddress = string.Empty;
    }

    /// <summary>Whether this episode should still be dropping packets at <paramref name="now"/>.</summary>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    /// <returns>
    /// True while the ban has been neither lifted nor run out — and, for a timed ban, only while at
    /// least <see cref="ShortestExpressibleRemainder"/> of it is left, since a shorter remainder
    /// cannot be asked for without meaning something else.
    /// </returns>
    public bool IsInForce(DateTimeOffset now)
    {
        if (LiftedAt is not null)
        {
            return false;
        }

        if (ExpiresAt is null)
        {
            return true;
        }

        return ExpiresAt.Value - now >= ShortestExpressibleRemainder;
    }

    /// <summary>How much of the ban is still to run at <paramref name="now"/>.</summary>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    /// <returns>
    /// The remaining duration, or <c>null</c> for a permanent ban. Zero or negative for an episode
    /// that has already run out — ask <see cref="IsInForce"/> first, which is the question a caller
    /// re-applying a ban actually has.
    /// </returns>
    /// <remarks>
    /// The REMAINING duration and not the original one. A panel restarted twenty-three hours into a
    /// twenty-four-hour ban that re-applied the original would hold the address for forty-seven
    /// hours, and a machine restarted often enough would hold it forever — a permanent ban assembled
    /// out of temporary ones, with nothing in the journal saying so.
    /// </remarks>
    public TimeSpan? RemainingTtl(DateTimeOffset now)
    {
        if (ExpiresAt is null)
        {
            return null;
        }

        return ExpiresAt.Value - now;
    }

    /// <summary>Moves when this episode runs out, because the host's element has just been moved.</summary>
    /// <param name="expiresAt">The new expiry, or <c>null</c> for a ban that now lasts until it is lifted.</param>
    /// <remarks>
    /// <para>
    /// <b>This exists because an nftables set is keyed by address.</b> A second <c>add element</c>
    /// for an address the set already holds REPLACES that element and its timeout — measured on
    /// nftables v1.0.9 and documented on the agent's <c>ban_address</c> — so after a re-ban the host
    /// holds exactly one element with exactly one expiry. A panel that answered with a second row
    /// would hold two, and the older one's expiry would then be a statement about this host that is
    /// not true. These rows are the only record of a ban that exists anywhere, so a row that
    /// disagrees with the kernel is not a cosmetic problem: it is the evidence being wrong.
    /// </para>
    /// <para>
    /// <b><see cref="BannedAt"/> is deliberately not moved.</b> The episode began when the address
    /// was first put out, and that is the instant the escalation ladder counts from; re-stamping it
    /// would let a re-ban walk an old offence forward out of the ladder's window. Who asked for the
    /// extension, and when, is recorded by the audit journal, which is where an actor belongs.
    /// </para>
    /// <para>
    /// <b>Only an episode still in force may be moved</b>, which is the caller's job to establish
    /// (<see cref="IsInForce"/>). Moving a finished one would erase a completed episode from the
    /// history the ladder reads, and the address would never escalate.
    /// </para>
    /// </remarks>
    public void Reschedule(DateTimeOffset? expiresAt)
    {
        ExpiresAt = expiresAt;
    }

    /// <summary>Records that the ban was lifted before it ran out.</summary>
    /// <param name="at">When it was lifted, taken from <see cref="IClock"/>.</param>
    /// <remarks>
    /// The first lift wins: a second one changes nothing rather than moving the instant, because the
    /// interesting fact is when the address was let back in, not when somebody last pressed the
    /// button.
    /// </remarks>
    public void Lift(DateTimeOffset at)
    {
        if (LiftedAt is not null)
        {
            return;
        }

        LiftedAt = at;
    }
}
