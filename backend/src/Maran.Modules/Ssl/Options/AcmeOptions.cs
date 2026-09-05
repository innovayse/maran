using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Ssl.Options;

/// <summary>
/// Settings for ordering certificates from an ACME certificate authority. Bound from the
/// <c>Acme</c> configuration section and validated at startup, so a missing contact address or an
/// unusable directory URL fails the boot rather than the first customer's order.
/// </summary>
/// <remarks>
/// The default directory is Let's Encrypt's STAGING endpoint, deliberately. A developer looping on a
/// bug orders the same certificate a dozen times, and production issuance is rate-limited per
/// registered domain per week — burning that budget makes the panel unable to issue for a real
/// customer for days, from a mistake nobody notices until then. Staging issues untrusted
/// certificates, which is exactly the right feedback: the flow is exercised end to end and the
/// browser says out loud that this is not a real certificate. A server's own value is written by the
/// installer into <c>panel.env</c>.
/// </remarks>
public sealed class AcmeOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Acme";

    /// <summary>The name the ACME <see cref="System.Net.Http.HttpClient"/> is registered under.</summary>
    public const string HttpClientName = "acme";

    /// <summary>
    /// The authority's directory document — the one URL an ACME client is configured with, from
    /// which every other endpoint is discovered.
    /// </summary>
    [Required]
    [Url]
    public string DirectoryUrl { get; set; } = "https://acme-staging-v02.api.letsencrypt.org/directory";

    /// <summary>
    /// The operator's contact address, registered with the ACME account so the authority can warn
    /// about expiries and account problems. An operator address, never a customer's.
    /// </summary>
    /// <remarks>
    /// Presence is checked here; the SHAPE is checked by
    /// <c>Validators/AcmeOptionsValidator.cs</c> against the panel's one definition of a
    /// valid address (<c>Maran.SharedKernel.Utilities.Mail.EmailAddressRule</c>). The
    /// <c>[EmailAddress]</c> annotation that used to stand here was a third, laxer answer to a
    /// question two other modules were already answering differently.
    /// </remarks>
    [Required]
    public string ContactEmail { get; set; } = "admin@localhost";

    /// <summary>
    /// Where the agent keeps certificate material. Operator-facing and outside every account's home:
    /// a customer may not read the key that serves their own site, because a site's PHP runs as that
    /// customer and would then be able to read it.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string CertificateStorePath { get; set; } = "/var/lib/maran/certs";

    /// <summary>How long one call to the authority may take, in seconds.</summary>
    /// <remarks>
    /// Far longer than an agent call: an authority is a public service across the internet, and its
    /// slow path is measured in tens of seconds. Cutting an order off early does not save anything —
    /// the order still exists at the authority and still counts against the rate limit.
    /// </remarks>
    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>How long to keep polling one authorization or order before giving up, in seconds.</summary>
    /// <remarks>
    /// A bound, not a preference. An authority answers "pending" until it has fetched the challenge
    /// file, and a client that loops while pending with no ceiling hangs forever the day validation
    /// never completes — which is the ordinary case for a domain whose DNS does not point here yet.
    /// </remarks>
    [Range(1, 600)]
    public int ValidationTimeoutSeconds { get; set; } = 60;

    /// <summary>How long to wait between polls of a pending authorization or order, in seconds.</summary>
    [Range(1, 60)]
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary><see cref="RequestTimeoutSeconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan RequestTimeout
    {
        get
        {
            return TimeSpan.FromSeconds(RequestTimeoutSeconds);
        }
    }

    /// <summary><see cref="ValidationTimeoutSeconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan ValidationTimeout
    {
        get
        {
            return TimeSpan.FromSeconds(ValidationTimeoutSeconds);
        }
    }

    /// <summary><see cref="PollIntervalSeconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan PollInterval
    {
        get
        {
            return TimeSpan.FromSeconds(PollIntervalSeconds);
        }
    }
}
