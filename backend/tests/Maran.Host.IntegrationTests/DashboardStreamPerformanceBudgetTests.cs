using Maran.Host.IntegrationTests.Fixtures;
using Maran.Host.IntegrationTests.Traits;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The live dashboard's cost promise, held where it can be observed: ten streams open at once ask the
/// agent for the host's readings once per tick, not ten times.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a count and not a time.</b> What would make ten tabs ten times as expensive is each stream
/// polling the agent for itself; the wall-clock cost of that on a real host is not measurable on a
/// shared runner. The agent's call counter is: if each stream polled, ten streams that have each seen
/// two readings would have made about twenty calls, and the shared feed makes about two.
/// </para>
/// <para>
/// <b>Where it runs.</b> <see cref="PerformanceBudgetAttribute"/> keeps it off the pull request path: it
/// runs on the push to <c>dev</c> and nightly, against its own baseline row (issue #289).
/// </para>
/// <para>
/// UNOBSERVED HERE: the API's processor and memory cost with ten tabs on a real host, and the
/// allocations per stream. The bound below is on calls to the agent, which is the part that scales
/// with streams if the design is wrong.
/// </para>
/// </remarks>
[PerformanceBudget]
[Collection(SharedDatabase.Name)]
public sealed class DashboardStreamPerformanceBudgetTests : IAsyncLifetime
{
    /// <summary>How many streams are open at once.</summary>
    private const int Streams = 10;

    /// <summary>How many readings each stream waits for before the calls are counted.</summary>
    private const int ReadingsPerStream = 2;

    /// <summary>
    /// The most agent calls the whole run may have made: one per tick for the readings every stream saw,
    /// one in flight, and one of slack. Ten streams polling for themselves would make about twenty.
    /// </summary>
    private const int MaxAgentCalls = ReadingsPerStream + 2;

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public DashboardStreamPerformanceBudgetTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Ten streams open at once cost the agent one call per tick and not one per stream.</summary>
    [Fact]
    public async Task Ten_streams_open_at_once_cost_the_agent_one_call_per_tick_and_not_one_per_stream()
    {
        var agent = new CountingAgentMonitorClient();
        await using var factory = DashboardStreamHost.CreateFactory(
            _pg, agent, configure: options =>
            {
                options.PerUserStreamLimit = Streams;
            });
        await DashboardStreamHost.PrepareAsync(factory);
        using var administrator = await EndpointHarness.SignInAsync(factory, "admin");
        using var deadline = new CancellationTokenSource(DashboardStreamHost.ReadTimeout);

        var responses = new List<HttpResponseMessage>();
        var readers = new List<DashboardSseReader>();
        try
        {
            for (var index = 0; index < Streams; index++)
            {
                var response = await DashboardStreamHost.OpenAsync(administrator, string.Empty, deadline.Token);
                responses.Add(response);
                readers.Add(await DashboardSseReader.OpenAsync(response, deadline.Token));
            }

            await Task.WhenAll(readers.Select(reader =>
            {
                return reader.ReadUntilAsync(
                    read =>
                    {
                        return read.Count(@event =>
                        {
                            return @event.Name == "metrics";
                        }) >= ReadingsPerStream;
                    },
                    deadline.Token);
            }));

            // The control: the feed did poll. A count of zero would satisfy the bound below for the
            // wrong reason, a feed that never asks the agent anything.
            Assert.True(agent.MetricsCalls >= ReadingsPerStream);
            Assert.True(
                agent.MetricsCalls <= MaxAgentCalls,
                $"{agent.MetricsCalls} agent calls for {Streams} streams that each saw {ReadingsPerStream} readings.");
        }
        finally
        {
            await deadline.CancelAsync();
            readers.ForEach(reader =>
            {
                reader.Dispose();
            });
            responses.ForEach(response =>
            {
                response.Dispose();
            });
        }
    }
}
