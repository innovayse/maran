namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// Source texts of planted marketplace files — the violations <c>MarketplaceOutboundIsGatedTests</c> must
/// find, and the clean ones it must not.
/// </summary>
/// <remarks>
/// Text and not real classes: a real file in the module that created its own HTTP client would be a defect
/// in the product, which is why the plant is a string the scanner reads exactly as it reads a source file.
/// </remarks>
public static class PlantedMarketplaceSources
{
    /// <summary>A service that builds its own HTTP client, bypassing the transport.</summary>
    public const string OwnHttpClient = """
        namespace Maran.Modules.Marketplace.Services;

        public sealed class SneakyFetcher
        {
            public async Task<string> FetchAsync()
            {
                using var client = new HttpClient();
                return await client.GetStringAsync("https://elsewhere.example/ping");
            }
        }
        """;

    /// <summary>A service that asks the client factory directly, also bypassing the transport.</summary>
    public const string OwnClientFactory = """
        public sealed class SneakyFactoryUser
        {
            private readonly IHttpClientFactory _factory;
        }
        """;

    /// <summary>A hosted service that would call the marketplace on a timer.</summary>
    public const string BackgroundCaller = """
        public sealed class MarketplacePoller : BackgroundService
        {
            protected override Task ExecuteAsync(CancellationToken stoppingToken)
            {
                return Task.CompletedTask;
            }
        }
        """;

    /// <summary>A service that is clean: it names the transport seam, mentions the words only in comments, and registers a named client.</summary>
    public const string Clean = """
        // The transport owns the HttpClient; this type never touches one.
        /* IHttpClientFactory is the transport's business, not this type's. */
        /// <summary>Asks the seam, whose HttpClient lives elsewhere.</summary>
        public sealed class Polite
        {
            private readonly IMarketplaceTransport _transport;

            public void Register(IServiceCollection services)
            {
                services.AddHttpClient("marketplace");
                var url = "https://api.example.test/api/v1";
            }
        }
        """;
}
