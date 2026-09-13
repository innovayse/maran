using Maran.Modules.Ssl.Domain.Enums;

namespace Maran.Modules.Ssl.Common;

/// <summary>What the panel tells a customer about one of their certificates.</summary>
/// <remarks>
/// Every field here is a fact about the certificate's LIFECYCLE, and none is material. There is no
/// certificate body and no key: the customer's browser can read the certificate off their own site,
/// and the key is not theirs to fetch — a site's PHP runs as that customer, so a key the API would
/// hand back is a key any script on the site could ask for.
/// </remarks>
/// <param name="Id">The certificate's identity.</param>
/// <param name="SiteId">The site it is installed for.</param>
/// <param name="Domain">The domain it was issued for.</param>
/// <param name="Source">Where it came from, and therefore whether the panel renews it.</param>
/// <param name="NotAfter">When it expires.</param>
/// <param name="IssuedAt">When the panel installed it.</param>
/// <param name="LastRenewalAttemptAt">When renewal last tried, or null if it never has.</param>
/// <param name="LastRenewalErrorCode">
/// The machine-stable code of the last renewal failure, or the empty string. A code, translated by
/// the panel like every other error code — never a sentence an authority wrote.
/// </param>
/// <param name="ConsecutiveRenewalFailures">How many renewal attempts have failed in a row.</param>
public sealed record CertificateDto(
    Guid Id,
    Guid SiteId,
    string Domain,
    CertificateSource Source,
    DateTimeOffset NotAfter,
    DateTimeOffset IssuedAt,
    DateTimeOffset? LastRenewalAttemptAt,
    string LastRenewalErrorCode,
    int ConsecutiveRenewalFailures);
