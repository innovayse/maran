using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Interfaces;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// The one answer to "can a wildcard certificate be ordered for this domain now": the account's plan, the DNS
/// module and the zone, asked in that order.
/// </summary>
/// <remarks>
/// Asked by both the issuance command, which refuses on it, and the issuance-options query, which shows it
/// to the customer. Two callers with one answer is the point: the checkbox the screen offers and the request
/// the panel accepts cannot disagree about why a wildcard is not possible.
/// </remarks>
public sealed class WildcardAvailabilityCheck
{
    /// <summary>Whether the account's plan sells DNS zones.</summary>
    private readonly IAccountFeatureDirectory _features;

    /// <summary>Whether the DNS module serves the domain's zone.</summary>
    private readonly IAcmeDnsChallengeWriter _dns;

    /// <summary>Creates the check.</summary>
    /// <param name="features">Answers whether the account's plan includes a feature.</param>
    /// <param name="dns">Answers whether the domain's challenge can be answered through DNS.</param>
    public WildcardAvailabilityCheck(IAccountFeatureDirectory features, IAcmeDnsChallengeWriter dns)
    {
        _features = features;
        _dns = dns;
    }

    /// <summary>Tells whether a wildcard certificate can be ordered for a domain of the account.</summary>
    /// <param name="accountId">The account that owns the domain's site.</param>
    /// <param name="domain">The domain.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns><see cref="WildcardAvailability.Available"/>, or the first thing in the way.</returns>
    public async Task<WildcardAvailability> CheckAsync(
        Guid accountId,
        string domain,
        CancellationToken cancellationToken)
    {
        if (!await _features.IncludesAsync(accountId, AccountFeature.Dns, cancellationToken))
        {
            return WildcardAvailability.FeatureNotIncluded;
        }

        return await _dns.CheckAsync(accountId, domain, cancellationToken);
    }
}
