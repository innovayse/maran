namespace Maran.Sdk.Contracts;

/// <summary>
/// Names of the rate-limit policies the Host registers, so a module can opt an endpoint into one
/// with <c>[EnableRateLimiting]</c>.
/// </summary>
/// <remarks>
/// The names live in the Sdk because both sides need them and only one side may depend on the
/// other: the Host defines what each policy does, a module says which one applies to its endpoint,
/// and a module can never reference the Host. Before this, the one module that needed a policy
/// spelled the name as a string literal — a typo would have silently applied no limit at all,
/// because an unknown policy name is a startup error only for endpoints, and a quiet one to miss.
/// </remarks>
public static class RateLimitPolicies
{
    /// <summary>The general API limit, applied to ordinary panel endpoints.</summary>
    public const string Api = "api";

    /// <summary>The authentication limit: tighter, partitioned per address and username, with a lockout.</summary>
    public const string Login = "login";

    /// <summary>
    /// The password-reset limit: its own bucket, keyed by the caller's address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate policy rather than a reuse of <see cref="Login"/>, and that is deliberate in both
    /// directions. Sharing a bucket with sign-in would let an attacker exhaust somebody's login
    /// budget by asking for resets, and — the other way round — would let reset requests hide inside
    /// a login allowance that is tuned for people who mistype. What this endpoint spends is not a
    /// guess but an OUTGOING MESSAGE with the operator's own return address on it: an unlimited one
    /// is a mail bomb aimed at any address the caller names and a fast route to the panel's domain
    /// being listed as a spam source.
    /// </para>
    /// <para>
    /// Kept separate from <see cref="Invitation"/> for the same reason spelled out there: recovery
    /// traffic recurring for the life of an account must not be able to exhaust the one-time budget
    /// a new customer needs to accept an invitation they cannot simply ask to have resent — that
    /// resend is an administrator-only action, unlike asking for another password-reset mail.
    /// </para>
    /// </remarks>
    public const string PasswordReset = "password-reset";

    /// <summary>
    /// The invitation-acceptance limit: its own bucket, keyed by the caller's address, separate
    /// from <see cref="PasswordReset"/> even though both endpoints are anonymous and both spend a
    /// token.
    /// </summary>
    /// <remarks>
    /// Sharing <see cref="PasswordReset"/>'s bucket would let unrelated traffic on either endpoint
    /// exhaust the other's budget, and the two failure modes are asymmetric enough that neither
    /// direction is acceptable. A customer behind shared NAT — an office, a campus, a carrier-grade
    /// NAT on a mobile network — whose neighbours are requesting password resets would find their
    /// one-time invitation link refused with no recovery: unlike a reset, they cannot simply ask for
    /// the invitation again themselves, because resending it is an administrator-only action
    /// (<c>ResendInvitationCommand</c>). And the other way round, an attacker probing invitation
    /// tokens from one address would burn the reset budget of every genuine customer sharing it.
    /// Accepting an invitation is a one-time onboarding step; requesting a reset is recovery traffic
    /// that recurs for the life of the account — the two do not belong in one meter.
    /// </remarks>
    public const string Invitation = "invitation";

    /// <summary>
    /// The site-log stream limit: a CONCURRENCY limit on how many tails one account may hold
    /// open at once, which is a different question from how fast it may open them.
    /// </summary>
    /// <remarks>
    /// A fixed-window limiter cannot answer it. Its lease returns no permit when the request ends —
    /// permits come back on the window timer — so it bounds the RATE of opening and says nothing
    /// about how many are open. A concurrency limiter's lease IS returned on disposal, which is the
    /// whole reason this is a separate policy rather than a bigger number on <see cref="Api"/>.
    /// </remarks>
    public const string SiteLogs = "site-logs";

    /// <summary>
    /// The restore limit: its own bucket, keyed by the caller's hosting account.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Api"/> because what it bounds is not load but the hazard of the
    /// operation itself — a restore replaces a live account's home and every one of its databases,
    /// and a caller repeating one is the thing worth refusing. Sharing the general budget would make
    /// it either meaningless or ruinous for every other screen.
    /// </remarks>
    public const string BackupRestore = "backup-restore";
}
