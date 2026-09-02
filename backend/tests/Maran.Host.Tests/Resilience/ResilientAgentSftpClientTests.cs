using Maran.Host.Resilience;
using Maran.SharedKernel.Security;
using Polly.Timeout;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the SFTP decorator does: every call goes through the pipeline, arguments unchanged.</summary>
/// <remarks>
/// One test per method, deletion included and named. Deletion is the shape of defect this repository
/// has already shipped: a single method inside a decorated class calling straight through to the
/// inner client, running with no timeout, while every one of the suite's tests passed.
/// </remarks>
public sealed class ResilientAgentSftpClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The password a creation or change call carries through the decorator.</summary>
    private static readonly SensitiveString Password = new("Qm4-brisk-otter-91");

    /// <summary>Creation retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Creation_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentSftpClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .CreateAsync("alice", "web", Password, default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Every creation argument reaches the inner client unchanged.</summary>
    [Fact]
    public async Task Every_creation_argument_reaches_the_inner_client_unchanged()
    {
        var inner = new RecordingAgentSftpClient();

        await Decorate(inner).CreateAsync("alice", "web", Password, default).WaitAsync(TestTimeout);

        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("web", inner.LastSftpUsername);
        Assert.Same(Password, inner.LastPassword);
    }

    /// <summary>The password change retries a transport failure through the pipeline with its arguments intact.</summary>
    [Fact]
    public async Task The_password_change_retries_a_transport_failure_through_the_pipeline_with_its_arguments_intact()
    {
        var inner = new RecordingAgentSftpClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .SetPasswordAsync("alice", "web", Password, default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("web", inner.LastSftpUsername);
        Assert.Same(Password, inner.LastPassword);
    }

    /// <summary>Deletion retries a transport failure through the pipeline and forwards both names.</summary>
    [Fact]
    public async Task Deletion_retries_a_transport_failure_through_the_pipeline_and_forwards_both_names()
    {
        var inner = new RecordingAgentSftpClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).DeleteAsync("alice", "web", default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("web", inner.LastSftpUsername);
    }

    /// <summary>A deletion that never returns is abandoned by the pipelines timeout.</summary>
    /// <remarks>
    /// The exact defect this repository already found, asserted directly: deletion once ran outside
    /// the pipeline with no timeout at all, so a wedged agent hung the HTTP request that ordered the
    /// removal. A retry test alone would not have caught a decorator that forwarded without a
    /// timeout; this one would.
    /// </remarks>
    [Fact]
    public async Task A_deletion_that_never_returns_is_abandoned_by_the_pipelines_timeout()
    {
        var inner = new RecordingAgentSftpClient { Hangs = true };

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await Decorate(inner).DeleteAsync("alice", "web", default).WaitAsync(TestTimeout);
        });
    }

    /// <summary>Wraps the recording client in the decorator under the real pipeline.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorated client.</returns>
    private static ResilientAgentSftpClient Decorate(RecordingAgentSftpClient inner)
    {
        return new ResilientAgentSftpClient(inner, OperationPipelineRegistry.WithOperationTimeout(1));
    }
}
