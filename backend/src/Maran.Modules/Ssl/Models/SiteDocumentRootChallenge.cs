namespace Maran.Modules.Ssl.Models;

/// <summary>
/// A site's challenge: the file goes into the document root of the domain being validated, written
/// by the agent under the owning account's uid (rules/security.md item 3).
/// </summary>
/// <param name="AccountUsername">System username of the account whose document root answers.</param>
public sealed record SiteDocumentRootChallenge(string AccountUsername) : AcmeChallengeTarget;
