namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Sites module immediately after a site has been created and its row committed, so that a
/// module which keeps something per site can set it up without Sites referencing it (the Dns module creates
/// the site's zone).
/// </summary>
/// <remarks>
/// <para>
/// <b>Past tense, and invoked inline.</b> By the time it is announced the vhost exists on the host and the
/// row is committed, so no subscriber can undo the creation and none may make it fail: the publisher
/// (<c>SiteCreationAnnouncer</c>) logs a subscriber's failure and returns. It is invoked in-process rather
/// than published to a queue so that a subscriber runs as the caller who created the site and reads through
/// that caller's tenant scope, which a queued message would lose.
/// </para>
/// <para>
/// <b>A lost announcement is acceptable.</b> Whatever a subscriber would have set up is also reachable by
/// hand (a customer can create the zone from the DNS screen), and the subscriber checks everything again
/// when it runs. The event carries no secret.
/// </para>
/// <para>
/// There is deliberately no <c>SiteDeleted</c> counterpart: nothing keeps state that must end with a site.
/// A site's DNS zone outlives it by decision, because mail and other records may live in the zone.
/// </para>
/// </remarks>
/// <param name="SiteId">The site that was created.</param>
/// <param name="AccountId">The account that owns the site.</param>
/// <param name="Domain">The site's primary domain, as stored.</param>
public sealed record SiteCreated(Guid SiteId, Guid AccountId, string Domain);
