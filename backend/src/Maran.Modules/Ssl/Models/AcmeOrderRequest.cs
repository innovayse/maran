namespace Maran.Modules.Ssl.Models;

/// <summary>What one ACME order needs to know, and nothing about the panel's own rows.</summary>
/// <remarks>
/// The order is placed for a domain, and the challenge that proves control of that domain is a file
/// written by the agent — never by the API (rules/security.md item 3) — at the place
/// <paramref name="Challenge"/> names: a site's document root, or the panel's own challenge directory.
/// Both facts travel together because neither is useful alone.
/// </remarks>
/// <param name="Domain">The domain the certificate is ordered for.</param>
/// <param name="Challenge">Where the file answering the HTTP-01 challenge is placed.</param>
public sealed record AcmeOrderRequest(string Domain, AcmeChallengeTarget Challenge);
