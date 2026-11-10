namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// The innermost handler of the panel's named marketplace client in the tests: it never opens a socket and
/// hands every request to a <see cref="FakeMarketplace"/>, which counts it and answers it. Because it sits
/// under the whole client pipeline, a request that reaches the network in production reaches the count here.
/// </summary>
public sealed class FakeMarketplaceHandler : HttpMessageHandler
{
    /// <summary>The marketplace that answers.</summary>
    private readonly FakeMarketplace _marketplace;

    /// <summary>Creates the handler.</summary>
    /// <param name="marketplace">The marketplace that counts and answers every request.</param>
    public FakeMarketplaceHandler(FakeMarketplace marketplace)
    {
        _marketplace = marketplace;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_marketplace.Respond(request));
    }
}
