using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MailService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentMailClient"/> that records every call as a line of text and answers success unless a
/// test arranges a failure for one named call. Secrets are never recorded: the line names the call and its
/// non-secret arguments only.
/// </summary>
public sealed class RecordingMailAgent : IAgentMailClient
{
    /// <summary>Failures to answer with, by call name (for example <c>SetMailbox</c>); consumed once each.</summary>
    private readonly Dictionary<string, Error> _failures = [];

    /// <summary>Every call made so far, oldest first, as <c>Name(arguments)</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The last mailbox arguments received, kept whole so a test can inspect the hash without printing it.</summary>
    public AgentMailboxArguments? LastMailbox { get; private set; }

    /// <summary>The last domain arguments received, kept whole so a test can inspect the key without printing it.</summary>
    public AgentMailDomainArguments? LastDomain { get; private set; }

    /// <summary>The last alias set received.</summary>
    public IReadOnlyList<AgentMailAlias>? LastAliases { get; private set; }

    /// <summary>The last catch-all targets received.</summary>
    public IReadOnlyList<string>? LastCatchAll { get; private set; }

    /// <summary>The last autoresponder received, or <c>null</c> when the last call cleared it.</summary>
    public AgentMailAutoresponder? LastAutoresponder { get; private set; }

    /// <summary>The status every status-shaped call answers with.</summary>
    public AgentMailStatus Status { get; set; } = new(
        [],
        AgentMailObservation.Yes,
        "mail.example.com",
        AgentMailObservation.Yes,
        2,
        4,
        AgentMailPasswordScheme.Argon2id,
        AgentMailObservation.Yes,
        0,
        AgentMailObservation.Yes);

    /// <summary>The queue the listing answers with.</summary>
    public AgentMailQueueListing Queue { get; set; } = new([], 0, false);

    /// <summary>The usage readings the usage call answers with.</summary>
    public List<AgentMailboxUsage> Usage { get; } = [];

    /// <summary>Arranges the next call named <paramref name="call"/> to fail with <paramref name="error"/>.</summary>
    /// <param name="call">The call's name without arguments.</param>
    /// <param name="error">The typed failure to answer with.</param>
    public void FailNext(string call, Error error)
    {
        _failures[call] = error;
    }

    /// <inheritdoc />
    public Task<Result<AgentMailStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Answer("GetStatus", string.Empty, Status));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailStatus>> EnableAsync(
        AgentMailServerSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Answer(
            "Enable",
            $"{settings.Hostname},pop3={settings.Pop3Enabled},av={settings.AntivirusEnabled},size={settings.MessageSizeMib}",
            Status));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailStatus>> DisableAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Answer("Disable", string.Empty, Status));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailStatus>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Answer("ReloadTls", string.Empty, Status));
    }

    /// <inheritdoc />
    public Task<Result<bool>> ConfigureDomainAsync(
        AgentMailDomainArguments arguments,
        CancellationToken cancellationToken)
    {
        LastDomain = arguments;

        return Task.FromResult(Answer(
            "ConfigureDomain",
            $"{arguments.AccountUsername},{arguments.Domain},{arguments.DkimSelector}",
            true));
    }

    /// <inheritdoc />
    public Task<Result> RemoveDomainAsync(string accountUsername, string domain, CancellationToken cancellationToken)
    {
        return Task.FromResult(AnswerPlain("RemoveDomain", $"{accountUsername},{domain}"));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailboxChange>> SetMailboxAsync(
        AgentMailboxArguments arguments,
        CancellationToken cancellationToken)
    {
        LastMailbox = arguments;

        return Task.FromResult(Answer(
            "SetMailbox",
            $"{arguments.AccountUsername},{arguments.LocalPart}@{arguments.Domain},quota={arguments.QuotaMib},"
            + $"limit={arguments.SendLimitPerHour},disabled={arguments.Disabled},hash={arguments.PasswordHash is not null}",
            new AgentMailboxChange(true, arguments.PasswordHash is not null)));
    }

    /// <inheritdoc />
    public Task<Result<bool>> RemoveMailboxAsync(
        string accountUsername,
        string domain,
        string localPart,
        bool deleteData,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Answer(
            "RemoveMailbox",
            $"{accountUsername},{localPart}@{domain},deleteData={deleteData}",
            deleteData));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailAliasOutcome>> SetAliasesAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<AgentMailAlias> aliases,
        IReadOnlyList<string>? catchAllTargets,
        CancellationToken cancellationToken)
    {
        LastAliases = aliases;
        LastCatchAll = catchAllTargets;

        return Task.FromResult(Answer(
            "SetAliases",
            $"{accountUsername},{domain},aliases={aliases.Count},catchAll={catchAllTargets is not null}",
            new AgentMailAliasOutcome((uint)aliases.Count, catchAllTargets is not null)));
    }

    /// <inheritdoc />
    public Task<Result<bool>> SetAutoresponderAsync(
        string accountUsername,
        string domain,
        string localPart,
        AgentMailAutoresponder? autoresponder,
        CancellationToken cancellationToken)
    {
        LastAutoresponder = autoresponder;

        return Task.FromResult(Answer(
            "SetAutoresponder",
            $"{accountUsername},{localPart}@{domain},set={autoresponder is not null}",
            autoresponder is not null));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailSuspensionOutcome>> SetAccountSuspensionAsync(
        string accountUsername,
        bool suspended,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Answer(
            "SetAccountSuspension",
            $"{accountUsername},suspended={suspended}",
            new AgentMailSuspensionOutcome(0, 0)));
    }

    /// <inheritdoc />
    public Task<Result<AgentAccountMailRemoval>> RemoveAccountMailAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Answer("RemoveAccountMail", accountUsername, new AgentAccountMailRemoval(0, 0)));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentMailboxUsage>>> ListMailboxUsageAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Answer<IReadOnlyList<AgentMailboxUsage>>(
            "ListMailboxUsage",
            accountUsername,
            Usage.ToList()));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailQueueListing>> ListQueueAsync(uint limit, CancellationToken cancellationToken)
    {
        return Task.FromResult(Answer("ListQueue", $"limit={limit}", Queue));
    }

    /// <inheritdoc />
    public Task<Result> DeleteQueuedMessageAsync(string queueId, CancellationToken cancellationToken)
    {
        return Task.FromResult(AnswerPlain("DeleteQueuedMessage", queueId));
    }

    /// <inheritdoc />
    public Task<Result<uint?>> FlushQueueAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Answer<uint?>("FlushQueue", string.Empty, 0u));
    }

    /// <summary>Every local submission binding sent, in order.</summary>
    /// <remarks>Kept apart from <c>Calls</c> so the call lists the other handlers' tests assert stay exact.</remarks>
    public List<AgentLocalSubmission> LocalSubmissions { get; } = [];

    /// <summary>The account of every local submission removal, in order.</summary>
    public List<string> LocalSubmissionRemovals { get; } = [];

    /// <inheritdoc />
    public Task<Result> SetLocalSubmissionAsync(AgentLocalSubmission submission, CancellationToken cancellationToken)
    {
        LocalSubmissions.Add(submission);

        return Task.FromResult(_failures.Remove("SetLocalSubmission", out var error) ? Result.Fail(error) : Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> RemoveLocalSubmissionAsync(string accountUsername, CancellationToken cancellationToken)
    {
        LocalSubmissionRemovals.Add(accountUsername);

        return Task.FromResult(
            _failures.Remove("RemoveLocalSubmission", out var error) ? Result.Fail(error) : Result.Ok());
    }

    /// <summary>Records a call and answers with its arranged failure, if any, else with <paramref name="value"/>.</summary>
    /// <typeparam name="T">The success value's type.</typeparam>
    /// <param name="call">The call's name.</param>
    /// <param name="arguments">The non-secret arguments, as text.</param>
    /// <param name="value">The success value.</param>
    /// <returns>The result.</returns>
    private Result<T> Answer<T>(string call, string arguments, T value)
    {
        Calls.Add($"{call}({arguments})");

        return _failures.Remove(call, out var error) ? Result<T>.Fail(error) : Result<T>.Ok(value);
    }

    /// <summary>Records a call without a value and answers with its arranged failure, if any.</summary>
    /// <param name="call">The call's name.</param>
    /// <param name="arguments">The non-secret arguments, as text.</param>
    /// <returns>The result.</returns>
    private Result AnswerPlain(string call, string arguments)
    {
        Calls.Add($"{call}({arguments})");

        return _failures.Remove(call, out var error) ? Result.Fail(error) : Result.Ok();
    }
}
