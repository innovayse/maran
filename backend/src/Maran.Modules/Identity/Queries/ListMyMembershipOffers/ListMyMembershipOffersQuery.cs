namespace Maran.Modules.Identity.Queries.ListMyMembershipOffers;

/// <summary>
/// Lists the open offers to join a hosting account's team that were made to the caller. It takes no
/// parameter: the login is the caller's own, read from their token, never from a request.
/// </summary>
public sealed record ListMyMembershipOffersQuery;
