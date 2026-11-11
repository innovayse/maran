using Maran.Agent.Client.Services.MailService;
using Maran.Host.Resilience;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the mail decorator does: every call goes through the pipeline, arguments unchanged.</summary>
/// <remarks>
/// Each test asserts both halves the decorator can get wrong: that the call was EXECUTED through the
/// pipeline — visible as a retried transport failure — and that every argument reached the inner client
/// unchanged. A method that called straight through to the inner client would run with no timeout at all
/// while the call site saw nothing wrong.
/// </remarks>
public sealed class ResilientAgentMailClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The settings an enable call carries through the decorator.</summary>
    private static readonly AgentMailServerSettings Settings =
        new("mail.example.test", false, false, 50, 3, 6, 15, false, new SensitiveString("hash-of-a-master-secret"));

    /// <summary>The domain arguments a configure call carries through the decorator.</summary>
    private static readonly AgentMailDomainArguments Domain =
        new("alice", "example.test", "s1", new SensitiveString("key"));

    /// <summary>The mailbox arguments a mailbox call carries through the decorator.</summary>
    private static readonly AgentMailboxArguments Mailbox =
        new("alice", "example.test", "info", null, 1024, false, 100, AgentMailSpamAction.FileToJunk);

    /// <summary>A transport outage on a mailbox removal is answered as an unavailable agent, not thrown.</summary>
    [Fact]
    public async Task A_transport_outage_on_a_mailbox_removal_answers_agent_unavailable_instead_of_throwing()
    {
        var inner = new RecordingAgentMailClient { FailuresBeforeSuccess = int.MaxValue };

        var result = await Decorate(inner)
            .RemoveMailboxAsync("alice", "example.test", "info", true, default)
            .WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Equal(3, inner.Calls);
    }

    /// <summary>Every lifecycle call retries a refused connection once and then succeeds.</summary>
    [Fact]
    public async Task The_lifecycle_calls_retry_a_transport_failure_through_the_pipeline()
    {
        var results = new List<(bool Ok, int Calls)>();
        foreach (var call in new Func<ResilientAgentMailClient, Task<bool>>[]
        {
            async client => { return (await client.GetStatusAsync(default)).IsSuccess; },
            async client => { return (await client.DisableAsync(default)).IsSuccess; },
            async client => { return (await client.ReloadTlsAsync(default)).IsSuccess; },
            async client => { return (await client.FlushQueueAsync(default)).IsSuccess; },
        })
        {
            var inner = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };

            var ok = await call(Decorate(inner)).WaitAsync(TestTimeout);

            results.Add((ok, inner.Calls));
        }

        Assert.All(results, row => { Assert.Equal((true, 2), row); });
    }

    /// <summary>Enable retries through the pipeline with its settings intact.</summary>
    [Fact]
    public async Task Enable_retries_a_transport_failure_through_the_pipeline_with_its_settings_intact()
    {
        var inner = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).EnableAsync(Settings, default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Same(Settings, inner.LastSettings);
    }

    /// <summary>An enable that never returns is abandoned by the pipeline's timeout, entered once.</summary>
    /// <remarks>
    /// Enable renders four daemons' configuration and starts them, so it is the call most able to sit
    /// still on a wedged host. It is entered exactly once: the timeout does not stop the agent's work,
    /// so a retry would start a second enabling beside the first.
    /// </remarks>
    [Fact]
    public async Task An_enable_that_never_returns_is_abandoned_by_the_pipelines_timeout_and_not_started_twice()
    {
        var inner = new RecordingAgentMailClient { Hangs = true };

        var result = await Decorate(inner).EnableAsync(Settings, default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>The domain calls retry through the pipeline with their arguments intact.</summary>
    [Fact]
    public async Task The_domain_calls_retry_through_the_pipeline_with_their_arguments_intact()
    {
        var configure = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };
        var remove = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };

        var configured = await Decorate(configure).ConfigureDomainAsync(Domain, default).WaitAsync(TestTimeout);
        var removed = await Decorate(remove).RemoveDomainAsync("alice", "example.test", default).WaitAsync(TestTimeout);

        Assert.True(configured.IsSuccess);
        Assert.Equal(2, configure.Calls);
        Assert.Same(Domain, configure.LastDomainArguments);
        Assert.True(removed.IsSuccess);
        Assert.Equal(2, remove.Calls);
        Assert.Equal("alice", remove.LastAccountUsername);
        Assert.Equal("example.test", remove.LastDomain);
    }

    /// <summary>The mailbox calls retry through the pipeline with their arguments intact.</summary>
    [Fact]
    public async Task The_mailbox_calls_retry_through_the_pipeline_with_their_arguments_intact()
    {
        var set = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };
        var remove = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };

        var changed = await Decorate(set).SetMailboxAsync(Mailbox, default).WaitAsync(TestTimeout);
        var removed = await Decorate(remove)
            .RemoveMailboxAsync("alice", "example.test", "info", true, default)
            .WaitAsync(TestTimeout);

        Assert.True(changed.IsSuccess);
        Assert.Equal(2, set.Calls);
        Assert.Same(Mailbox, set.LastMailboxArguments);
        Assert.True(removed.IsSuccess);
        Assert.Equal(2, remove.Calls);
        Assert.Equal(("alice", "example.test", "info", true),
            (remove.LastAccountUsername, remove.LastDomain, remove.LastLocalPart, remove.LastDeleteData));
    }

    /// <summary>The alias and autoresponder calls retry through the pipeline with their arguments intact.</summary>
    [Fact]
    public async Task The_alias_and_autoresponder_calls_retry_through_the_pipeline_with_their_arguments_intact()
    {
        var aliases = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };
        var responder = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };
        IReadOnlyList<AgentMailAlias> list = [new AgentMailAlias("sales", ["a@example.test"])];
        IReadOnlyList<string> catchAll = ["c@example.test"];
        var reply = new AgentMailAutoresponder("Away", "Back soon", null, null, 2);

        var set = await Decorate(aliases).SetAliasesAsync("alice", "example.test", list, catchAll, default)
            .WaitAsync(TestTimeout);
        var installed = await Decorate(responder)
            .SetAutoresponderAsync("alice", "example.test", "info", reply, default)
            .WaitAsync(TestTimeout);

        Assert.True(set.IsSuccess);
        Assert.Equal(2, aliases.Calls);
        Assert.Same(list, aliases.LastAliases);
        Assert.Same(catchAll, aliases.LastCatchAllTargets);
        Assert.True(installed.IsSuccess);
        Assert.Equal(2, responder.Calls);
        Assert.Same(reply, responder.LastAutoresponder);
        Assert.Equal("info", responder.LastLocalPart);
    }

    /// <summary>The account-wide calls retry through the pipeline with their arguments intact.</summary>
    [Fact]
    public async Task The_account_wide_calls_retry_through_the_pipeline_with_their_arguments_intact()
    {
        var suspend = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };
        var removal = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };
        var usage = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };

        var suspended = await Decorate(suspend).SetAccountSuspensionAsync("alice", true, default).WaitAsync(TestTimeout);
        var removed = await Decorate(removal).RemoveAccountMailAsync("alice", default).WaitAsync(TestTimeout);
        var measured = await Decorate(usage).ListMailboxUsageAsync("alice", default).WaitAsync(TestTimeout);

        Assert.True(suspended.IsSuccess && removed.IsSuccess && measured.IsSuccess);
        Assert.Equal([2, 2, 2], new[] { suspend.Calls, removal.Calls, usage.Calls });
        Assert.True(suspend.LastSuspended);
        Assert.Equal("alice", suspend.LastAccountUsername);
        Assert.Equal("alice", removal.LastAccountUsername);
        Assert.Equal("alice", usage.LastAccountUsername);
    }

    /// <summary>The local submission calls retry through the pipeline with their arguments intact.</summary>
    [Fact]
    public async Task The_local_submission_calls_retry_through_the_pipeline_with_their_arguments_intact()
    {
        var set = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };
        var remove = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };
        var submission = new AgentLocalSubmission("alice", ["a.test"], 100, ["info@a.test"]);

        var applied = await Decorate(set).SetLocalSubmissionAsync(submission, default).WaitAsync(TestTimeout);
        var removed = await Decorate(remove).RemoveLocalSubmissionAsync("alice", default).WaitAsync(TestTimeout);

        Assert.True(applied.IsSuccess && removed.IsSuccess);
        Assert.Equal([2, 2], new[] { set.Calls, remove.Calls });
        Assert.Same(submission, set.LastSubmission);
        Assert.Equal("alice", remove.LastAccountUsername);
    }

    /// <summary>The queue calls retry through the pipeline with their arguments intact.</summary>
    [Fact]
    public async Task The_queue_calls_retry_through_the_pipeline_with_their_arguments_intact()
    {
        var list = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };
        var delete = new RecordingAgentMailClient { FailuresBeforeSuccess = 1 };

        var listed = await Decorate(list).ListQueueAsync(25, default).WaitAsync(TestTimeout);
        var deleted = await Decorate(delete).DeleteQueuedMessageAsync("4F2A9C01B7D", default).WaitAsync(TestTimeout);

        Assert.True(listed.IsSuccess && deleted.IsSuccess);
        Assert.Equal([2, 2], new[] { list.Calls, delete.Calls });
        Assert.Equal(25u, list.LastLimit);
        Assert.Equal("4F2A9C01B7D", delete.LastQueueId);
    }

    /// <summary>A mailbox removal that never returns is abandoned, and entered once.</summary>
    [Fact]
    public async Task A_mailbox_removal_that_never_returns_is_abandoned_rather_than_started_a_second_time()
    {
        var inner = new RecordingAgentMailClient { Hangs = true };

        var result = await Decorate(inner)
            .RemoveMailboxAsync("alice", "example.test", "info", true, default)
            .WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>Wraps the recording client in the decorator under the real pipeline.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorated client.</returns>
    private static ResilientAgentMailClient Decorate(RecordingAgentMailClient inner)
    {
        return new ResilientAgentMailClient(inner, OperationPipelineRegistry.WithOperationTimeout(1));
    }
}
