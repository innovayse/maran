using Maran.Agent.Client.Services.FtpsService;
using Maran.Host.Resilience;
using Maran.SharedKernel.Security;
using Polly.Timeout;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the FTPS decorator does: every call goes through the pipeline, arguments unchanged.</summary>
/// <remarks>
/// One test per method, and each asserts both halves the decorator can get wrong: that the call was
/// EXECUTED through the pipeline — visible as a retried transport failure — and that every argument
/// reached the inner client unchanged. A method that called straight through to the inner client
/// would run with no timeout at all while the call site saw nothing wrong; that is the shape of
/// defect this repository has already shipped once.
/// </remarks>
public sealed class ResilientAgentFtpsClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The password a change call carries through the decorator.</summary>
    private static readonly SensitiveString Password = new("Vd2-amber-heron-77");

    /// <summary>The arguments a creation call carries through the decorator.</summary>
    private static readonly CreateFtpsUserArguments Arguments =
        new("alice", "files", new SensitiveString("Qm4-brisk-otter-91"));

    /// <summary>Enable retries a transport failure through the pipeline with every field intact.</summary>
    [Fact]
    public async Task Enable_retries_a_transport_failure_through_the_pipeline_with_every_field_intact()
    {
        var inner = new RecordingAgentFtpsClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .EnableAsync("ftp.example.test", 30000, 30100, "203.0.113.7", 42, default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("ftp.example.test", inner.LastHostname);
        Assert.Equal(30000u, inner.LastPassivePortMin);
        Assert.Equal(30100u, inner.LastPassivePortMax);
        Assert.Equal("203.0.113.7", inner.LastPassiveAddress);
        Assert.Equal(42u, inner.LastMaxClients);
    }

    /// <summary>An enable that never returns is abandoned by the pipelines timeout.</summary>
    /// <remarks>
    /// Enable renders and applies a configuration and can restart the daemon, so it is the call most
    /// able to sit still on a wedged host. Without the timeout the operator's click never comes back.
    /// </remarks>
    [Fact]
    public async Task An_enable_that_never_returns_is_abandoned_by_the_pipelines_timeout()
    {
        var inner = new RecordingAgentFtpsClient { Hangs = true };

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await Decorate(inner)
                .EnableAsync("ftp.example.test", 30000, 30100, string.Empty, 42, default)
                .WaitAsync(TestTimeout);
        });
    }

    /// <summary>Disable retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Disable_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentFtpsClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).DisableAsync(default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>The status call retries a transport failure through the pipeline and forwards the hostname.</summary>
    [Fact]
    public async Task The_status_call_retries_a_transport_failure_through_the_pipeline_and_forwards_the_hostname()
    {
        var inner = new RecordingAgentFtpsClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).GetStatusAsync("ftp.example.test", default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("ftp.example.test", inner.LastHostname);
    }

    /// <summary>The TLS reload retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task The_tls_reload_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentFtpsClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).ReloadTlsAsync(default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Creation retries a transport failure through the pipeline with its arguments intact.</summary>
    [Fact]
    public async Task Creation_retries_a_transport_failure_through_the_pipeline_with_its_arguments_intact()
    {
        var inner = new RecordingAgentFtpsClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).CreateUserAsync(Arguments, default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Same(Arguments, inner.LastArguments);
    }

    /// <summary>The password change retries a transport failure through the pipeline with its arguments intact.</summary>
    [Fact]
    public async Task The_password_change_retries_a_transport_failure_through_the_pipeline_with_its_arguments_intact()
    {
        var inner = new RecordingAgentFtpsClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner)
            .SetPasswordAsync("alice", "files", Password, default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("files", inner.LastFtpsUsername);
        Assert.Same(Password, inner.LastPassword);
    }

    /// <summary>Deletion retries a transport failure through the pipeline and forwards both names.</summary>
    [Fact]
    public async Task Deletion_retries_a_transport_failure_through_the_pipeline_and_forwards_both_names()
    {
        var inner = new RecordingAgentFtpsClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).DeleteUserAsync("alice", "files", default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("alice", inner.LastAccountUsername);
        Assert.Equal("files", inner.LastFtpsUsername);
    }

    /// <summary>A deletion that never returns is abandoned by the pipelines timeout.</summary>
    [Fact]
    public async Task A_deletion_that_never_returns_is_abandoned_by_the_pipelines_timeout()
    {
        var inner = new RecordingAgentFtpsClient { Hangs = true };

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await Decorate(inner).DeleteUserAsync("alice", "files", default).WaitAsync(TestTimeout);
        });
    }

    /// <summary>A creation the agent never answers is abandoned rather than started a second time.</summary>
    /// <remarks>
    /// Two claims in one assertion, and the second is the one that was learned expensively. The
    /// call is abandoned — so a wedged agent cannot hang the request. And the inner client was
    /// entered exactly ONCE: the panel's timeout does not stop the agent's work, only its watching
    /// of it, so a retry on the timeout would have started a second creation beside the first and
    /// manufactured the overlapping-call condition out of one customer request.
    /// </remarks>
    [Fact]
    public async Task A_creation_the_agent_never_answers_is_abandoned_rather_than_started_a_second_time()
    {
        var inner = new RecordingAgentFtpsClient { Hangs = true };

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await Decorate(inner).CreateUserAsync(Arguments, default).WaitAsync(TestTimeout);
        });

        Assert.Equal(1, inner.Calls);
    }

    /// <summary>Wraps the recording client in the decorator under the real pipeline.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorated client.</returns>
    private static ResilientAgentFtpsClient Decorate(RecordingAgentFtpsClient inner)
    {
        return new ResilientAgentFtpsClient(inner, OperationPipelineRegistry.WithOperationTimeout(1));
    }
}
