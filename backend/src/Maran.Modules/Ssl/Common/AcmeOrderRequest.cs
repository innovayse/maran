namespace Maran.Modules.Ssl.Common;

/// <summary>What one ACME order needs to know, and nothing about the panel's own rows.</summary>
/// <remarks>
/// The order is placed for a domain, and the challenge that proves control of that domain is a file
/// inside the owning account's document root — written by the agent under that account's uid, never
/// by the API (rules/security.md item 3). Both facts travel together because neither is useful alone.
/// </remarks>
/// <param name="Domain">The domain the certificate is ordered for.</param>
/// <param name="AccountUsername">System username of the account whose document root answers the challenge.</param>
public sealed record AcmeOrderRequest(string Domain, string AccountUsername);
