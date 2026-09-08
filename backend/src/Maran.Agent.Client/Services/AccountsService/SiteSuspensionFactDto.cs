namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>What the host can be observed to be serving for one of an account's vhosts.</summary>
/// <remarks>
/// One entry per vhost the HOST holds for the account, not per site the panel remembers creating.
/// A vhost the panel has forgotten is exactly the one still serving a suspended customer's page, so
/// the answer is asked of the machine and a caller that compares it against its own list is
/// comparing against the weaker of the two.
/// </remarks>
/// <param name="Domain">The site's primary domain, as the vhost's file name spells it.</param>
/// <param name="ServingStub">
/// <c>true</c> only when the vhost on disk is byte for byte what the suspended template renders for
/// this site. The agent also answers <c>false</c> when it could not render the comparison text at
/// all — an unresolvable document root, a <c>server_name</c> line it could not read — so
/// unobservable arrives here as "not stubbed", which refuses a suspension rather than certifying
/// one.
/// </param>
public sealed record SiteSuspensionFactDto(string Domain, bool ServingStub);
