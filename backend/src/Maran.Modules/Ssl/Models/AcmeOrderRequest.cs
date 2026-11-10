namespace Maran.Modules.Ssl.Models;

/// <summary>What one ACME order needs to know, and nothing about the panel's own rows.</summary>
/// <remarks>
/// The order is placed for a domain, and the challenge that proves control of that domain is placed by
/// the panel at the place <paramref name="Challenge"/> names: a file in a site's document root or in the
/// panel's own challenge directory (written by the agent, never by the API — rules/security.md item 3), or
/// a TXT record in a zone this server serves. Both facts travel together because neither is useful alone.
/// </remarks>
/// <param name="Domain">The domain the certificate is ordered for.</param>
/// <param name="Challenge">Where the proof of control is placed.</param>
public sealed record AcmeOrderRequest(string Domain, AcmeChallengeTarget Challenge)
{
    /// <summary>
    /// Whether the order also asks for every name directly under the domain (<c>*.domain</c>). It is a
    /// property of the challenge and not a field beside it: a wildcard can only be proven over DNS-01, so
    /// an order whose proof is a DNS record is a wildcard order and no other order is, and the two cannot
    /// be stated differently.
    /// </summary>
    public bool CoversWildcard
    {
        get { return Challenge is DnsZoneChallenge; }
    }
}
