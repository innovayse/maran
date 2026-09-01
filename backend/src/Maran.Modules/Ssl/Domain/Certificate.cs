using Maran.Modules.Ssl.Domain.Enums;

namespace Maran.Modules.Ssl.Domain;

/// <summary>
/// The panel's record of one TLS certificate installed for one site: where it came from, when it
/// expires, and how its last unattended renewal went (spec §11).
/// </summary>
/// <remarks>
/// What is NOT here is the point of the type. No private key, and no certificate body: the material
/// lives in the agent's certificate store, outside every account's home, written and read only by
/// the agent. A key in this table would be a key in every backup of the panel database, in every
/// query an operator runs, and in the object graph of anything that ever logs an entity — and the
/// panel gains nothing from holding it, because the one operation that needs a key is an install,
/// and an install is handed fresh material by the caller that just obtained it.
///
/// A renewal failure is recorded as a machine code, never as the authority's or the agent's own
/// sentence: renewal runs unattended, so an operator has to be able to see WHY it keeps failing, and
/// a diagnostic that quoted the material it could not parse would put key bytes in this table
/// (rules/security.md item 8).
///
/// Nothing here has a public setter, for the same reason the site row has none: a field assigned
/// from outside is a field that can disagree with what is installed on disk.
/// </remarks>
public sealed class Certificate
{
    /// <summary>The longest a failing certificate waits between attempts.</summary>
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromDays(8);

    /// <summary>How many times the one-hour base may double before the cap applies.</summary>
    /// <remarks>
    /// Bounds the exponent itself, not just its result: <c>Math.Pow(2, 1000)</c> is infinity, and
    /// <c>TimeSpan.FromHours(infinity)</c> throws rather than saturating — so a certificate that had
    /// failed a few hundred times would take the whole renewal pass down with it.
    /// </remarks>
    private const int MaximumBackoffDoublings = 20;

    /// <summary>The certificate's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that owns it. Every tenant-scoped query is closed over this column.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The site this certificate is installed for, in the Sites module's terms.</summary>
    /// <remarks>
    /// A plain identity and not a navigation property: the sites table belongs to another module and
    /// another schema, which this one may not query (rules/architecture.md). The site is read through
    /// <c>ISiteDirectory</c> when its facts are needed.
    /// </remarks>
    public Guid SiteId { get; private set; }

    /// <summary>The domain the certificate was issued for; unique across the server, like the site's.</summary>
    public string Domain { get; private set; }

    /// <summary>Where it came from, and therefore whether renewal may replace it.</summary>
    public CertificateSource Source { get; private set; }

    /// <summary>When the installed certificate expires, as the agent parsed it out of the material.</summary>
    public DateTimeOffset NotAfter { get; private set; }

    /// <summary>When the panel installed this material.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>When renewal last tried this certificate, or <c>null</c> if it never has.</summary>
    public DateTimeOffset? LastRenewalAttemptAt { get; private set; }

    /// <summary>
    /// The machine-stable code of the last renewal failure, or the empty string when the last attempt
    /// succeeded or none has run. Never the authority's own text.
    /// </summary>
    public string LastRenewalErrorCode { get; private set; }

    /// <summary>How many renewal attempts have failed in a row. Reset to zero by a success.</summary>
    /// <remarks>
    /// The number an operator actually needs: one failure is a certificate authority having a bad
    /// minute, and eight in a row is a domain whose DNS no longer points here and a site that will
    /// stop serving on a date this row already names.
    /// </remarks>
    public int ConsecutiveRenewalFailures { get; private set; }

    /// <summary>Records a freshly installed certificate.</summary>
    /// <param name="id">The certificate's identity.</param>
    /// <param name="accountId">The account that owns it.</param>
    /// <param name="siteId">The site it is installed for.</param>
    /// <param name="domain">The domain it was issued for.</param>
    /// <param name="source">Where it came from.</param>
    /// <param name="notAfter">When it expires, as parsed from the installed material.</param>
    /// <param name="issuedAt">When the panel installed it, taken from <see cref="IClock"/>.</param>
    public Certificate(
        Guid id,
        Guid accountId,
        Guid siteId,
        string domain,
        CertificateSource source,
        DateTimeOffset notAfter,
        DateTimeOffset issuedAt)
    {
        Id = id;
        AccountId = accountId;
        SiteId = siteId;
        Domain = domain;
        Source = source;
        NotAfter = notAfter;
        IssuedAt = issuedAt;
        LastRenewalAttemptAt = null;
        LastRenewalErrorCode = string.Empty;
        ConsecutiveRenewalFailures = 0;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Certificate()
    {
        Domain = string.Empty;
        LastRenewalErrorCode = string.Empty;
    }

    /// <summary>Replaces the installed material with newly issued material.</summary>
    /// <param name="notAfter">When the new certificate expires.</param>
    /// <param name="renewedAt">The instant of the renewal, taken from <see cref="IClock"/>.</param>
    /// <remarks>Clears the failure state: a certificate that has just been renewed is not failing,
    /// whatever happened on the previous attempts.</remarks>
    public void Renewed(DateTimeOffset notAfter, DateTimeOffset renewedAt)
    {
        NotAfter = notAfter;
        IssuedAt = renewedAt;
        LastRenewalAttemptAt = renewedAt;
        LastRenewalErrorCode = string.Empty;
        ConsecutiveRenewalFailures = 0;
    }

    /// <summary>Records a renewal attempt that did not produce new material.</summary>
    /// <param name="errorCode">The machine-stable code of the failure. Never a supplied sentence.</param>
    /// <param name="attemptedAt">The instant of the attempt, taken from <see cref="IClock"/>.</param>
    /// <remarks>
    /// The installed material and its <see cref="NotAfter"/> are deliberately left alone: the old
    /// certificate is still on disk and still serving, and it is its real expiry — not the failed
    /// attempt — that decides when the site goes dark.
    /// </remarks>
    public void RenewalFailed(string errorCode, DateTimeOffset attemptedAt)
    {
        LastRenewalAttemptAt = attemptedAt;
        LastRenewalErrorCode = errorCode;
        ConsecutiveRenewalFailures += 1;
    }

    /// <summary>Whether this certificate is due for renewal at <paramref name="now"/>.</summary>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    /// <param name="window">How far ahead of expiry renewal starts.</param>
    /// <returns><c>true</c> when the panel may and should re-order this certificate now.</returns>
    /// <remarks>
    /// This is THE definition of "due", and the renewal job calls it rather than restating it: an
    /// earlier version had the job repeat the window in a LINQ predicate with nothing enforcing that
    /// the two agreed, so mutating either left the other green.
    ///
    /// Three conditions, each of which has to hold on its own.
    ///
    /// The certificate must be one the panel ordered — re-ordering a customer's uploaded certificate
    /// would destroy material the panel cannot obtain again.
    ///
    /// It must be inside the window. An expired certificate is still due: the site is already broken
    /// and re-ordering is the fix, so the window has no lower bound.
    ///
    /// And a certificate whose renewal keeps failing must have served its backoff. That third
    /// condition is not tidiness. Every customer on this server shares ONE ACME registration, and an
    /// authority meters failed authorizations and new orders per account — so one domain whose DNS
    /// has moved away, re-ordered on every pass for ever, spends a budget every other customer's
    /// issuance draws on. Backoff turns "one dead domain costs the server an order a day for ever"
    /// into a handful of attempts that still fit comfortably inside the thirty-day window.
    /// </remarks>
    public bool IsDueForRenewal(DateTimeOffset now, TimeSpan window)
    {
        if (Source != CertificateSource.Acme || NotAfter > now + window)
        {
            return false;
        }

        return now >= NextAttemptAllowedAt();
    }

    /// <summary>The earliest instant a further renewal attempt may be made.</summary>
    /// <returns>
    /// The last attempt plus the current backoff, or <see cref="DateTimeOffset.MinValue"/> when
    /// nothing has failed yet — a certificate with a clean record waits for nothing.
    /// </returns>
    /// <remarks>
    /// Doubling from one hour and capped at eight days. Over a thirty-day window that is roughly
    /// eight attempts rather than thirty daily ones: few enough to be kind to the shared account, and
    /// many enough that a domain repaired at any point still renews before it expires. The cap
    /// matters as much as the growth — uncapped doubling passes "longer than the whole window" after
    /// a dozen failures, and a certificate that stopped retrying before its own expiry would expire
    /// while the panel believed it was still trying.
    /// </remarks>
    public DateTimeOffset NextAttemptAllowedAt()
    {
        if (ConsecutiveRenewalFailures <= 0 || LastRenewalAttemptAt is not { } lastAttempt)
        {
            return DateTimeOffset.MinValue;
        }

        var doublings = Math.Min(ConsecutiveRenewalFailures - 1, MaximumBackoffDoublings);
        var backoff = TimeSpan.FromHours(Math.Pow(2, doublings));

        return lastAttempt + (backoff < MaximumBackoff ? backoff : MaximumBackoff);
    }
}
