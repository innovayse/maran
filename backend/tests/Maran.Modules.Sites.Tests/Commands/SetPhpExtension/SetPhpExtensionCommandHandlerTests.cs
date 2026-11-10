using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Commands.SetPhpExtension;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.Commands.SetPhpExtension;

/// <summary>
/// Proves what switching an extension does with each shape of answer the agent can give it, and that
/// every outcome is journalled.
/// </summary>
/// <remarks>
/// The cases worth naming are the ones that look like success and are not: a stream that ends with no
/// terminal event, and a stream that says "changed" over an extension that is not in the state asked
/// for. Neither is an observation that the host changed (rules/testing.md). Administrator-only is the
/// controller's policy, pinned in <c>SitesControllerPolicyTests</c> and over HTTP in
/// <c>PhpExtensionEndpointTests</c>.
/// </remarks>
public sealed class SetPhpExtensionCommandHandlerTests
{
    /// <summary>Switching an available extension on runs the change, reports it, and journals the success.</summary>
    [Fact]
    public async Task Enabling_an_available_extension_runs_the_change_and_journals_the_success()
    {
        var php = Host();
        php.ExtensionEvents.AddRange(
        [
            new PhpExtensionEvent(PhpExtensionEventKind.Progress, 40, "install", string.Empty, string.Empty, false, null),
            new PhpExtensionEvent(PhpExtensionEventKind.Changed, 100, "verify", "8.3", "gd", true, null),
        ]);
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();

        var result = await Handler(php, tasks, audit).HandleAsync(Command("gd", true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([("8.3", "gd", true)], php.SetExtensionCalls);
        var gd = result.Value.Single(extension => { return extension.Name == "gd"; });
        Assert.True(gd.Enabled);
        Assert.Equal("Images (GD)", gd.Label);
        Assert.Equal(ExtensionAvailability.Available, gd.Availability);
        Assert.Equal("Can be switched on and off", gd.AvailabilityName);

        var task = tasks.Tasks.Single();
        Assert.Equal(TaskKinds.PhpExtensionChange, task.Kind);
        Assert.Equal("8.3 gd", task.Subject);
        // A host-wide change concerns no account and is no customer's to read.
        Assert.Null(task.AccountId);
        Assert.Equal(TaskAudience.AdministratorOnly, task.Audience);
        Assert.True(task.Completed);
        Assert.Contains(task.Reports, report => { return report.Percent == 40 && report.Line == "install"; });

        var entry = audit.Entries.Single();
        Assert.Equal(SiteAuditActions.PhpExtensionChanged, entry.Action);
        Assert.Equal("8.3: gd enabled", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Equal("tests", entry.UserAgent);
        Assert.NotEqual(Guid.Empty, entry.ActorUserId);
    }

    /// <summary>Switching a loaded extension off runs the change and answers the new state.</summary>
    [Fact]
    public async Task Disabling_a_loaded_extension_runs_the_change_and_answers_the_new_state()
    {
        var php = Host();
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();

        var result = await Handler(php, tasks, audit).HandleAsync(Command("opcache", false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([("8.3", "opcache", false)], php.SetExtensionCalls);
        Assert.False(result.Value.Single(extension => { return extension.Name == "opcache"; }).Enabled);
        Assert.Equal("8.3: opcache disabled", audit.Entries.Single().Subject);
        Assert.True(tasks.Tasks.Single().Completed);
    }

    /// <summary>An extension already in the requested state is a success, runs nothing, and is journalled.</summary>
    [Fact]
    public async Task An_extension_already_in_the_requested_state_is_a_success_and_nothing_is_run()
    {
        var php = Host();
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();

        var result = await Handler(php, tasks, audit).HandleAsync(Command("opcache", true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(php.SetExtensionCalls);
        // Nothing ran, so there is nothing for an operator to watch.
        Assert.Empty(tasks.Tasks);
        Assert.True(audit.Entries.Single().Succeeded);
    }

    /// <summary>
    /// A request the host's OS family cannot honour is refused before a task is begun or the agent is
    /// asked, and journalled as a failure.
    /// </summary>
    /// <param name="extension">The catalogue name asked for.</param>
    /// <param name="enable">The state asked for.</param>
    [Theory]
    [InlineData("redis", true)]
    [InlineData("curl", false)]
    [InlineData("zip", false)]
    [InlineData("imagick", true)]
    public async Task A_request_the_host_cannot_honour_is_refused_before_anything_runs(string extension, bool enable)
    {
        var php = Host();
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();

        var result = await Handler(php, tasks, audit).HandleAsync(Command(extension, enable), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PhpExtensionNotAvailable", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(php.SetExtensionCalls);
        Assert.Empty(tasks.Tasks);
        Assert.False(audit.Entries.Single().Succeeded);
    }

    /// <summary>An extension that can only be switched on is switched on, though it can never be switched off.</summary>
    [Fact]
    public async Task An_extension_that_can_only_be_switched_on_is_switched_on()
    {
        var php = Host();
        var tasks = new RecordingTaskRecorder();

        var result = await Handler(php, tasks, new RecordingAuditWriter()).HandleAsync(
            Command("mbstring", true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([("8.3", "mbstring", true)], php.SetExtensionCalls);
    }

    /// <summary>A name the agent's own list does not carry is refused as not allowed.</summary>
    [Fact]
    public async Task A_name_the_agents_list_does_not_carry_is_refused_as_not_allowed()
    {
        var php = Host();
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();

        var result = await Handler(php, tasks, audit).HandleAsync(Command("soap", true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PhpExtensionNotAllowed", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(php.SetExtensionCalls);
        Assert.Empty(tasks.Tasks);
        Assert.False(audit.Entries.Single().Succeeded);
    }

    /// <summary>An agent that cannot say what the version loads is not asked to change it.</summary>
    [Fact]
    public async Task An_unreadable_list_refuses_before_anything_is_changed()
    {
        var php = new RecordingAgentPhpClient(Error.Of("AgentUnavailable", ErrorType.Unavailable));
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();

        var result = await Handler(php, tasks, audit).HandleAsync(Command("gd", true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Empty(php.SetExtensionCalls);
        Assert.Empty(tasks.Tasks);
        Assert.False(audit.Entries.Single().Succeeded);
    }

    /// <summary>
    /// A failure the agent names reaches the caller and the task exactly as the client typed it, code
    /// and kind, and is journalled as a failure.
    /// </summary>
    /// <param name="code">The code the client stamped on the failed event.</param>
    /// <param name="kind">The kind the client decided for it.</param>
    [Theory]
    [InlineData("AgentPackageSourceRefused", ErrorType.UpstreamRefused)]
    [InlineData("AgentInvalidInput", ErrorType.Validation)]
    [InlineData("AgentUnavailable", ErrorType.Unavailable)]
    [InlineData("AgentSystemFailure", ErrorType.Failure)]
    public async Task A_named_failure_is_passed_through_with_its_code_and_its_kind(string code, ErrorType kind)
    {
        var php = Host();
        php.ExtensionEvents.AddRange(
        [
            new PhpExtensionEvent(PhpExtensionEventKind.Progress, 10, "install", string.Empty, string.Empty, false, null),
            new PhpExtensionEvent(PhpExtensionEventKind.Failed, 0, string.Empty, string.Empty, string.Empty, false, Error.Of(code, kind)),
        ]);
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();

        var result = await Handler(php, tasks, audit).HandleAsync(Command("gd", true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(kind, result.Error.Type);
        Assert.Equal(code, tasks.Tasks.Single().FailureCode);
        Assert.False(tasks.Tasks.Single().Completed);
        Assert.False(audit.Entries.Single().Succeeded);
        Assert.Equal("8.3: gd enabled", audit.Entries.Single().Subject);
    }

    /// <summary>A stream that ends without a terminal event is a failure, not a quiet success.</summary>
    [Fact]
    public async Task A_stream_that_ends_with_no_terminal_event_is_not_reported_as_changed()
    {
        var php = Host();
        php.ExtensionEvents.Add(
            new PhpExtensionEvent(PhpExtensionEventKind.Progress, 70, "reload", string.Empty, string.Empty, false, null));
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();

        var result = await Handler(php, tasks, audit).HandleAsync(Command("gd", true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PhpExtensionToggleFailed", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal("PhpExtensionToggleFailed", tasks.Tasks.Single().FailureCode);
        Assert.False(audit.Entries.Single().Succeeded);
    }

    /// <summary>A stream that ends in a terminal kind which states no outcome is a failure.</summary>
    /// <param name="kind">A terminal kind that is neither a change nor a named failure.</param>
    [Theory]
    [InlineData(PhpExtensionEventKind.Dropped)]
    [InlineData(PhpExtensionEventKind.Idle)]
    [InlineData(PhpExtensionEventKind.Truncated)]
    [InlineData(PhpExtensionEventKind.Cancelled)]
    public async Task A_terminal_kind_that_states_no_outcome_is_a_failure(PhpExtensionEventKind kind)
    {
        var php = Host();
        php.ExtensionEvents.Add(new PhpExtensionEvent(kind, 0, string.Empty, string.Empty, string.Empty, false, null));
        var tasks = new RecordingTaskRecorder();

        var result = await Handler(php, tasks, new RecordingAuditWriter()).HandleAsync(
            Command("gd", true),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PhpExtensionToggleFailed", result.Error!.Code);
    }

    /// <summary>
    /// A stream that says "changed" over an extension that is not in the requested state fails: the
    /// state is read back from the agent, not taken from the stream's word.
    /// </summary>
    [Fact]
    public async Task A_changed_event_over_an_unchanged_state_is_not_reported_as_changed()
    {
        var php = Host();
        php.AppliesExtensionChange = false;
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();

        var result = await Handler(php, tasks, audit).HandleAsync(Command("gd", true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PhpExtensionToggleFailed", result.Error!.Code);
        Assert.Equal("PhpExtensionToggleFailed", tasks.Tasks.Single().FailureCode);
        Assert.False(tasks.Tasks.Single().Completed);
        Assert.False(audit.Entries.Single().Succeeded);
    }

    /// <summary>
    /// The journal subject names the version, the catalogue name and the state, and nothing the agent
    /// produced: the same spelling on a success and on a failure.
    /// </summary>
    [Fact]
    public async Task The_journal_subject_is_spelled_the_same_on_success_and_on_failure()
    {
        var okAudit = new RecordingAuditWriter();
        await Handler(Host(), new RecordingTaskRecorder(), okAudit).HandleAsync(Command("gd", true), CancellationToken.None);

        var failedHost = Host();
        failedHost.AppliesExtensionChange = false;
        var failedAudit = new RecordingAuditWriter();
        await Handler(failedHost, new RecordingTaskRecorder(), failedAudit).HandleAsync(
            Command("gd", true),
            CancellationToken.None);

        Assert.Equal(okAudit.Entries.Single().Subject, failedAudit.Entries.Single().Subject);
        Assert.True(okAudit.Entries.Single().Succeeded);
        Assert.False(failedAudit.Entries.Single().Succeeded);
    }

    /// <summary>A host with version 8.3 loading opcache and curl, and these answers for what can be done.</summary>
    /// <remarks>
    /// gd and opcache can go both ways, mbstring and zip only on, redis and curl not at all, and the
    /// agent did not state imagick: every availability the panel distinguishes appears once.
    /// </remarks>
    private static RecordingAgentPhpClient Host()
    {
        var php = new RecordingAgentPhpClient("8.3");
        php.Extensions.AddRange(
        [
            new PhpExtensionDto("curl", true, PhpExtensionAvailability.NotPackaged),
            new PhpExtensionDto("gd", false, PhpExtensionAvailability.Available),
            new PhpExtensionDto("imagick", false, PhpExtensionAvailability.Unknown),
            new PhpExtensionDto("mbstring", false, PhpExtensionAvailability.DisableNotSupported),
            new PhpExtensionDto("opcache", true, PhpExtensionAvailability.Available),
            new PhpExtensionDto("redis", false, PhpExtensionAvailability.NotPackaged),
            new PhpExtensionDto("zip", true, PhpExtensionAvailability.DisableNotSupported),
        ]);

        return php;
    }

    /// <summary>A version that is not installed as an app is refused, journalled, and never reaches the agent.</summary>
    [Fact]
    public async Task A_version_that_is_not_installed_as_an_app_is_refused_and_never_reaches_the_agent()
    {
        var php = Host();
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();

        var result = await Handler(php, tasks, audit, SiteStacks.WithoutRuntimes()).HandleAsync(
            Command("gd", true),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AppsNotInstalled", result.Error!.Code);
        Assert.Empty(php.SetExtensionCalls);
        Assert.Empty(tasks.Tasks);
        Assert.False(Assert.Single(audit.Entries).Succeeded);
    }

    /// <summary>Builds the handler over the given doubles, acting as an administrator.</summary>
    /// <param name="php">The agent's PHP surface.</param>
    /// <param name="tasks">Records the long-running task.</param>
    /// <param name="audit">Receives this module's journal entries.</param>
    /// <param name="stack">The installed stack; a host with PHP 8.3 and 8.4 installed when omitted.</param>
    private static SetPhpExtensionCommandHandler Handler(
        RecordingAgentPhpClient php,
        RecordingTaskRecorder tasks,
        RecordingAuditWriter audit,
        SiteStackGate? stack = null)
    {
        return new SetPhpExtensionCommandHandler(
            php,
            tasks,
            new StubCorrelationIdAccessor("correlation-1"),
            new SiteAuditJournal(audit, FakeCurrentUser.Admin()),
            SitesTestContext.SiteNames(),
            stack ?? SiteStacks.Ready());
    }

    /// <summary>Builds the command for version 8.3, stamped as an administrator's request.</summary>
    /// <param name="extension">The catalogue name.</param>
    /// <param name="enabled">The state to put it in.</param>
    private static SetPhpExtensionCommand Command(string extension, bool enabled)
    {
        return new SetPhpExtensionCommand(enabled, "8.3", extension, "203.0.113.7", "tests");
    }
}
