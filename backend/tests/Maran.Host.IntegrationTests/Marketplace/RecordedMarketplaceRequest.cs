namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>One request that reached the fake marketplace, as the wire carried it.</summary>
/// <param name="Method">The HTTP verb.</param>
/// <param name="Host">The host the request was addressed to.</param>
/// <param name="Path">The absolute path, without the query.</param>
/// <param name="Authorization">The <c>Authorization</c> header as sent, or null when the request carried none.</param>
public sealed record RecordedMarketplaceRequest(string Method, string Host, string Path, string? Authorization);
