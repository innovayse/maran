using Maran.Agent.Client.Services.CronService;
using Maran.Host.Resilience;
using Polly.Timeout;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the cron decorator does: every call goes through the pipeline, arguments unchanged.</summary>
/// <remarks>
/// Each method has its own retry test rather than one test standing for the class. A method that
/// forgets the pipeline is invisible from its call site and from every other method's test — this
/// repository has already shipped exactly that, one undecorated method inside a decorated class,
/// with the whole suite green.
/// </remarks>
public sealed class ResilientAgentCronClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The schedule the tests that carry one send.</summary>
    private static readonly AgentCronSchedule Schedule = new("30", "3", "*", "*", "*");

    /// <summary>Listing retries a transport failure through the pipeline and forwards the account.</summary>
    [Fact]
    public async Task Listing_retries_a_transport_failure_through_the_pipeline_and_forwards_the_account()
    {
        var inner = new RecordingAgentCronClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).ListEntriesAsync("alice", default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
    }

    /// <summary>Creation retries a transport failure through the pipeline and forwards every argument.</summary>
    [Fact]
    public async Task Creation_retries_a_transport_failure_through_the_pipeline_and_forwards_every_argument()
    {
        var inner = new RecordingAgentCronClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .CreateEntryAsync("alice", Schedule, "/usr/bin/true", default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Same(Schedule, inner.LastSchedule);
        Assert.Equal("/usr/bin/true", inner.LastCommand);
    }

    /// <summary>Update retries a transport failure through the pipeline and forwards every argument.</summary>
    [Fact]
    public async Task Update_retries_a_transport_failure_through_the_pipeline_and_forwards_every_argument()
    {
        var inner = new RecordingAgentCronClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .UpdateEntryAsync("alice", "e1", Schedule, "/usr/bin/true", default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("e1", inner.LastEntryId);
        Assert.Same(Schedule, inner.LastSchedule);
        Assert.Equal("/usr/bin/true", inner.LastCommand);
    }

    /// <summary>Deletion retries a transport failure through the pipeline and forwards both names.</summary>
    [Fact]
    public async Task Deletion_retries_a_transport_failure_through_the_pipeline_and_forwards_both_names()
    {
        var inner = new RecordingAgentCronClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).DeleteEntryAsync("alice", "e1", default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("e1", inner.LastEntryId);
    }

    /// <summary>Switching an entry retries a transport failure and forwards the flag it was given.</summary>
    [Fact]
    public async Task Switching_an_entry_retries_a_transport_failure_and_forwards_the_flag_it_was_given()
    {
        var inner = new RecordingAgentCronClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .SetEntryEnabledAsync("alice", "e1", true, default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("e1", inner.LastEntryId);
        Assert.True(inner.LastEnabled);
    }

    /// <summary>The output read retries a transport failure through the pipeline and forwards both names.</summary>
    [Fact]
    public async Task The_output_read_retries_a_transport_failure_through_the_pipeline_and_forwards_both_names()
    {
        var inner = new RecordingAgentCronClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).GetEntryOutputAsync("alice", "e1", default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("e1", inner.LastEntryId);
    }

    /// <summary>The environment read retries a transport failure through the pipeline and forwards the account.</summary>
    [Fact]
    public async Task The_environment_read_retries_a_transport_failure_and_forwards_the_account()
    {
        var inner = new RecordingAgentCronClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).GetEnvironmentAsync("alice", default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
    }

    /// <summary>The environment write retries a transport failure and forwards the set it was given.</summary>
    [Fact]
    public async Task The_environment_write_retries_a_transport_failure_and_forwards_the_set_it_was_given()
    {
        var inner = new RecordingAgentCronClient { FailuresBeforeSuccess = 1 };
        IReadOnlyList<AgentCronEnvVar> variables = [new AgentCronEnvVar("TZ", "UTC")];

        var result = await Decorate(inner).SetEnvironmentAsync("alice", variables, default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Same(variables, inner.LastVariables);
    }

    /// <summary>A listing that never returns is abandoned by the pipelines timeout.</summary>
    /// <remarks>
    /// The behaviour the decorator exists for, asserted rather than assumed: a retry proves only that
    /// something wrapped the call, while an inner client that never returns proves the wrapper is a
    /// timeout. Chosen on the read-only method deliberately — a listing is the call most easily
    /// waved through as harmless, and a listing against a wedged host hangs the request exactly as a
    /// creation does.
    /// </remarks>
    [Fact]
    public async Task A_listing_that_never_returns_is_abandoned_by_the_pipelines_timeout()
    {
        var inner = new RecordingAgentCronClient { Hangs = true };

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await Decorate(inner).ListEntriesAsync("alice", default).WaitAsync(TestTimeout);
        });
    }

    /// <summary>Wraps the recording client in the decorator under the real pipeline.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorated client.</returns>
    private static ResilientAgentCronClient Decorate(RecordingAgentCronClient inner)
    {
        return new ResilientAgentCronClient(inner, OperationPipelineRegistry.WithOperationTimeout(1));
    }
}
