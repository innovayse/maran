using System.Diagnostics;
using System.Net;
using Maran.Host.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Resilience;

/// <summary>
/// The marketplace client's timeout backstop: attached as a handler on the named client, it lets a fast
/// answer through and ends a request that outlives the configured deadline plus its grace. The retry is
/// the transport's and is held up by the Marketplace module's own tests (the pipeline cannot tell the
/// single-use grant POST from a GET, which is why it holds no retry).
/// </summary>
public sealed class MarketplacePipelineTests
{
    /// <summary>An innermost handler that answers after a delay, honouring cancellation.</summary>
    private sealed class DelayedHandler : HttpMessageHandler
    {
        /// <summary>How long it waits before answering.</summary>
        private readonly TimeSpan _delay;

        /// <summary>Creates the handler.</summary>
        /// <param name="delay">How long it waits before answering.</param>
        public DelayedHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>Builds a client carrying the pipeline over the given innermost handler.</summary>
    /// <param name="inner">The handler that answers.</param>
    /// <param name="timeout">The configured per-attempt deadline the pipeline is built with.</param>
    /// <returns>A client from a real client factory with the pipeline attached.</returns>
    private static HttpClient ClientWith(HttpMessageHandler inner, TimeSpan timeout)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("marketplace-under-test")
            .ConfigurePrimaryHttpMessageHandler(() => { return inner; })
            .AddResilienceHandler(MarketplacePipeline.Name, (builder, _) => { MarketplacePipeline.Configure(builder, timeout); });

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("marketplace-under-test");
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    /// <summary>A request that finishes inside the deadline gets its answer through the pipeline.</summary>
    [Fact]
    public async Task A_request_inside_the_deadline_is_answered()
    {
        using var client = ClientWith(new DelayedHandler(TimeSpan.FromMilliseconds(10)), TimeSpan.FromSeconds(1));

        using var response = await client.GetAsync(new Uri("https://marketplace.test/x"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>A request that outlives the deadline plus the grace is ended by the pipeline, not left to hang.</summary>
    [Fact]
    public async Task A_request_that_outlives_the_deadline_and_grace_is_ended()
    {
        using var client = ClientWith(new DelayedHandler(Timeout.InfiniteTimeSpan), TimeSpan.FromMilliseconds(50));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<Exception>(() => { return client.GetAsync(new Uri("https://marketplace.test/x")); });

        Assert.True(
            watch.Elapsed < MarketplacePipeline.Grace + TimeSpan.FromSeconds(30),
            "The pipeline did not end the request.");
    }

    /// <summary>The backstop waits the grace beyond the configured deadline, so the transport's own deadline always fires first.</summary>
    [Fact]
    public async Task A_request_slower_than_the_deadline_but_inside_the_grace_is_still_answered()
    {
        using var client = ClientWith(new DelayedHandler(TimeSpan.FromMilliseconds(200)), TimeSpan.FromMilliseconds(50));

        using var response = await client.GetAsync(new Uri("https://marketplace.test/x"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
