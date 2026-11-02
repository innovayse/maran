namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>What the host can be observed to be serving for one of an account's vhosts.</summary>
/// <remarks>
/// One entry per vhost the HOST holds for the account, not per site the panel remembers creating.
/// A vhost the panel has forgotten is exactly the one still serving a suspended customer's page, so
/// the answer is asked of the machine and a caller that compares it against its own list is
/// comparing against the weaker of the two.
/// </remarks>
/// <param name="Domain">The site's primary domain, as the vhost's file name spells it.</param>
/// <param name="Serving">
/// What the vhost serves. <see cref="SiteServing.Unobservable"/> is a state of its own and not a
/// spelling of <see cref="SiteServing.NotStub"/>: a caller attesting a suspension refuses on both,
/// and a caller attesting a restart refuses on both, but only the first of the two tells the
/// operator that nothing was compared.
/// </param>
public sealed record SiteSuspensionFactDto(string Domain, SiteServing Serving);
