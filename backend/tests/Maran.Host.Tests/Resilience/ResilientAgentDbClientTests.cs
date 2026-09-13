using Maran.Host.Resilience;
using Maran.SharedKernel.Security;
using Polly.Timeout;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the database decorator does: every call goes through the pipeline, arguments unchanged.</summary>
/// <remarks>
/// Each method has its own retry test rather than one test standing for the class. A method that
/// forgets the pipeline is invisible from its call site and from every other method's test — this
/// repository has already shipped exactly that, one undecorated method inside a decorated class,
/// with the whole suite green.
/// </remarks>
public sealed class ResilientAgentDbClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The password a creation call carries through the decorator.</summary>
    private static readonly SensitiveString Password = new("Tz7-quiet-mule-42");

    /// <summary>Creation retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Creation_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentDbClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .CreateAsync("alice", "shop", "shopuser", Password, default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Every creation argument reaches the inner client unchanged.</summary>
    [Fact]
    public async Task Every_creation_argument_reaches_the_inner_client_unchanged()
    {
        var inner = new RecordingAgentDbClient();

        await Decorate(inner)
            .CreateAsync("alice", "shop", "shopuser", Password, default)
            .WaitAsync(TestTimeout);

        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("shop", inner.LastDatabaseName);
        Assert.Equal("shopuser", inner.LastDbUsername);
        Assert.Same(Password, inner.LastPassword);
    }

    /// <summary>Drop retries a transport failure through the pipeline and forwards all three names.</summary>
    [Fact]
    public async Task Drop_retries_a_transport_failure_through_the_pipeline_and_forwards_all_three_names()
    {
        var inner = new RecordingAgentDbClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .DropAsync("alice", "shop", "shopuser", default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("shop", inner.LastDatabaseName);
        Assert.Equal("shopuser", inner.LastDbUsername);
    }

    /// <summary>Listing retries a transport failure through the pipeline and forwards the account.</summary>
    [Fact]
    public async Task Listing_retries_a_transport_failure_through_the_pipeline_and_forwards_the_account()
    {
        var inner = new RecordingAgentDbClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).ListAsync("alice", default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
    }

    /// <summary>The size call retries a transport failure through the pipeline and forwards both names.</summary>
    [Fact]
    public async Task The_size_call_retries_a_transport_failure_through_the_pipeline_and_forwards_both_names()
    {
        var inner = new RecordingAgentDbClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).GetSizeAsync("alice", "shop", default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("shop", inner.LastDatabaseName);
    }

    /// <summary>Setting a password retries a transport failure through the pipeline and forwards every argument.</summary>
    [Fact]
    public async Task Setting_a_password_retries_a_transport_failure_through_the_pipeline_and_forwards_every_argument()
    {
        var inner = new RecordingAgentDbClient { FailuresBeforeSuccess = 1 };
        var password = new SensitiveString("Replaced-2026");

        var result = await Decorate(inner)
            .SetPasswordAsync("alice", "shopuser", password, default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("shopuser", inner.LastDbUsername);
        Assert.Same(password, inner.LastPassword);
    }

    /// <summary>A listing that never returns is abandoned by the pipelines timeout.</summary>
    /// <remarks>
    /// The behaviour the decorator exists for, asserted rather than assumed: a retry proves only that
    /// something wrapped the call, while an inner client that never returns proves the wrapper is a
    /// timeout. Chosen on the read-only method deliberately — a listing is the call most easily
    /// waved through as harmless, and a listing against a wedged database server hangs the request
    /// exactly as a creation does.
    /// </remarks>
    [Fact]
    public async Task A_listing_that_never_returns_is_abandoned_by_the_pipelines_timeout()
    {
        var inner = new RecordingAgentDbClient { Hangs = true };

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await Decorate(inner).ListAsync("alice", default).WaitAsync(TestTimeout);
        });
    }

    /// <summary>Wraps the recording client in the decorator under the real pipeline.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorated client.</returns>
    private static ResilientAgentDbClient Decorate(RecordingAgentDbClient inner)
    {
        return new ResilientAgentDbClient(inner, OperationPipelineRegistry.WithOperationTimeout(1));
    }
}
