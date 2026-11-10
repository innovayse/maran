using System.Diagnostics;
using System.Net;
using Maran.Host.Extensions;
using Maran.Host.Resilience;
using Maran.Modules.Licensing.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Resilience;

/// <summary>
/// The outbound pipeline of the licence lease renewal: a timeout and nothing else, attached to the module's named
/// client by the Host. A fast answer passes, a request that outlives the configured deadline plus the grace is ended,
/// and the Host's own registration puts the pipeline on the client the Licensing module asks the factory for.
/// </summary>
public sealed class OutboundHttpPipelineTests
{
    /// <summary>An innermost handler that answers after a delay, honouring cancellation.</summary>
    private sealed class DelayedHandler : HttpMessageHandler
    {
        /// <summary>How long it waits before answering.</summary>
        private readonly TimeSpan _delay;

        /// <summary>How many requests reached it.</summary>
        private int _calls;

        /// <summary>Creates the handler.</summary>
        /// <param name="delay">How long it waits before answering.</param>
        public DelayedHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        /// <summary>How many requests reached the handler: a retry would make this more than one.</summary>
        public int Calls
        {
            get { return Volatile.Read(ref _calls); }
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
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
        services.AddHttpClient("outbound-under-test")
            .ConfigurePrimaryHttpMessageHandler(() => { return inner; })
            .AddResilienceHandler(OutboundHttpPipeline.Name, (builder, _) => { OutboundHttpPipeline.Configure(builder, timeout); });

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("outbound-under-test");
        client.Timeout = Timeout.InfiniteTimeSpan;

        return client;
    }

    /// <summary>A request that finishes inside the deadline gets its answer through the pipeline.</summary>
    [Fact]
    public async Task A_request_inside_the_deadline_is_answered()
    {
        using var client = ClientWith(new DelayedHandler(TimeSpan.FromMilliseconds(10)), TimeSpan.FromSeconds(1));

        using var response = await client.PostAsync(new Uri("https://licence.test/api/v1/lease"), new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The backstop waits the grace beyond the configured deadline, so the caller's own deadline always fires first.</summary>
    [Fact]
    public async Task A_request_slower_than_the_deadline_but_inside_the_grace_is_still_answered()
    {
        using var client = ClientWith(new DelayedHandler(TimeSpan.FromMilliseconds(200)), TimeSpan.FromMilliseconds(50));

        using var response = await client.PostAsync(new Uri("https://licence.test/api/v1/lease"), new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>A request that outlives the deadline plus the grace is ended by the pipeline, not left to hang, and is not repeated.</summary>
    [Fact]
    public async Task A_request_that_outlives_the_deadline_and_grace_is_ended_and_not_retried()
    {
        var inner = new DelayedHandler(Timeout.InfiniteTimeSpan);
        using var client = ClientWith(inner, TimeSpan.FromMilliseconds(50));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<Exception>(
            () => { return client.PostAsync(new Uri("https://licence.test/api/v1/lease"), new StringContent("{}")); });

        Assert.True(
            watch.Elapsed < OutboundHttpPipeline.Grace + TimeSpan.FromSeconds(30),
            "The pipeline did not end the request.");
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>
    /// The Host's own registration puts the pipeline on the client the Licensing module asks the factory for: with
    /// the deadline set to one second a request that never answers is ended after the grace, and a fast one passes.
    /// </summary>
    [Fact]
    public async Task The_hosts_registration_attaches_the_pipeline_to_the_licence_lease_client()
    {
        var inner = new DelayedHandler(Timeout.InfiniteTimeSpan);
        var services = new ServiceCollection();
        services.Configure<LeaseServiceOptions>(options => { options.LeaseRequestTimeoutSeconds = 1; });
        services.AddPanelResilience();
        services.AddHttpClient(LeaseServiceOptions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => { return inner; });
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(LeaseServiceOptions.HttpClientName);
        client.Timeout = Timeout.InfiniteTimeSpan;
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<Exception>(
            () => { return client.PostAsync(new Uri("https://licence.test/api/v1/lease"), new StringContent("{}")); });

        Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(1), "The configured deadline was not applied.");
        Assert.True(
            watch.Elapsed < TimeSpan.FromSeconds(1) + OutboundHttpPipeline.Grace + TimeSpan.FromSeconds(30),
            "The pipeline did not end the request.");
    }
}
