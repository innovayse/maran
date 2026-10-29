namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// One route the panel deliberately serves to a caller who is not signed in, with the reason that
/// is safe.
/// </summary>
/// <param name="Route">The route key as <see cref="EndpointAuthorizationCensus.RouteKey"/> spells it, e.g. <c>POST /api/v1/auth/login</c>.</param>
/// <param name="Reason">Why nobody needs to be authenticated to call it; never blank.</param>
public sealed record PinnedAnonymousEndpoint(string Route, string Reason);
