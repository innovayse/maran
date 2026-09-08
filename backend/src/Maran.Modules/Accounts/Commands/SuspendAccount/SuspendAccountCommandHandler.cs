using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.Accounts.Commands.SuspendAccount;

/// <summary>Handles <see cref="SuspendAccountCommand"/> by stopping the account on the server.</summary>
public sealed class SuspendAccountCommandHandler
{
    /// <summary>
    /// Pre-compiled log delegate for a module that refused to stop what it runs. Source-generated
    /// because the reason belongs to the operator and never to the customer-facing message.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> LogCascadeRefused =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(SuspendAccountCommandHandler)),
            "Account {AccountName} was not suspended: a module refused to stop what it runs.");

    /// <summary>
    /// Pre-compiled log delegate for a host that cannot be observed to have stopped the account.
    /// </summary>
    /// <remarks>
    /// Error, not warning, and it names what was seen: this is the failure that used to be invisible.
    /// Nothing threw, so every earlier version of this handler wrote <c>Suspended</c> onto the row
    /// and answered success over websites that were still serving. What an operator needs is the
    /// list of what the host said.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogSuspensionUnobserved =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(2, nameof(SuspendAccountCommandHandler)),
            "Account {AccountName} was not suspended: the host does not show it stopped — {Observed}.");

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The agent, which owns everything outside the database.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>The bus the suspension cascade is announced on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where a subscriber's refusal and an unobserved suspension are written for an operator.</summary>
    private readonly ILogger<SuspendAccountCommandHandler> _logger;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>The panel-wide task journal, so an operator can watch this run instead of waiting on it.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The current request's correlation id, recorded on the task beside its stages.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="agent">The agent client that performs the privileged half and answers the attestation.</param>
    /// <param name="bus">The bus <see cref="AccountSuspending"/> is invoked on.</param>
    /// <param name="logger">Where a subscriber's refusal and an unobserved suspension are recorded.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    /// <param name="correlationIds">The current request's correlation id.</param>
    public SuspendAccountCommandHandler(
        AccountsDbContext dbContext,
        IAgentAccountsClient agent,
        IMessageBus bus,
        ILogger<SuspendAccountCommandHandler> logger,
        AccountAuditJournal journal,
        ITaskRecorder tasks,
        ICorrelationIdAccessor correlationIds)
    {
        _dbContext = dbContext;
        _agent = agent;
        _bus = bus;
        _logger = logger;
        _journal = journal;
        _tasks = tasks;
        _correlationIds = correlationIds;
    }

    /// <summary>Suspends the account. Idempotent: suspending a suspended account changes nothing.</summary>
    /// <remarks>
    /// <para>
    /// <b>What a suspension covers, as of this handler.</b> Four things, each driven by the module
    /// that owns it — this one may reference none of the other three, so each arrives through
    /// <see cref="AccountSuspending"/>:
    /// </para>
    /// <para>
    /// The account's own Linux login: <c>usermod --lock</c> and the nologin shell, so neither a
    /// password nor an SSH key already in place authenticates it. Every vhost the account owns,
    /// replaced by the suspended one so a visitor gets the suspension page instead of the customer's
    /// site. Every entry in the account's crontab, suppressed by a marker ORTHOGONAL to the
    /// per-entry <c>enabled</c> flag — that flag is the customer's own choice, and reusing it would
    /// make the resume switch back on the jobs they had turned off themselves. And every
    /// <c>&lt;account&gt;_*</c> SFTP login, locked: each is its own passwd entry sharing the
    /// account's uid, so the <c>usermod --lock</c> above reaches none of them, and until they were
    /// locked a suspended customer kept a working WRITE credential into their own home.
    /// </para>
    /// <para>
    /// <b>What it deliberately does NOT cover, stated here so nobody has to re-derive it.</b> The
    /// account's DATABASES keep accepting connections; revoking access is the only thing that would
    /// stop a suspended customer's data being read by an application they host elsewhere, and
    /// restoring the grants exactly is a real reversal risk, so it is an open product decision. The
    /// panel's own web LOGIN still works — sign-in consults the user's lockout and the password, and
    /// never the account's status. And FOREIGN crontab lines, which the panel did not write, keep
    /// firing: a crontab is not the panel's file, so they are counted and reported rather than
    /// deleted.
    /// </para>
    /// <para>
    /// <b>What COMPLETED is allowed to mean here.</b> Exactly this: the cascade was invoked and
    /// waited for, the agent locked the login, and the HOST was then asked what it is doing and
    /// answered that the login is locked, that every vhost it holds for this account is byte for
    /// byte the suspended one, that every managed entry of its crontab carries the suspension marker
    /// and that no SFTP login of the account is still unlocked. It does NOT mean the account is
    /// doing nothing on the server — the paragraph above is the standing set of exceptions, and they
    /// are reported on the task rather than left to this comment. Suspension completes on observed
    /// absence of service, not on the absence of an exception, which is the same standard the
    /// deletion cascade is held to.
    /// </para>
    /// <para>
    /// <b>Why the attestation is asked of the agent directly and not through an Sdk seam.</b> The
    /// deletion cascade verifies itself through <see cref="IAccountResidueAuditor"/> in the Sdk
    /// because its question — "does any composed module still hold rows" — can only be answered by
    /// the Host, which sees every module's model. This question is the opposite shape: suspension's
    /// residue is on the HOST, and it is one call on a client this module already holds and already
    /// declares in its manifest. Putting it behind an Sdk interface would mean either duplicating the
    /// agent client's DTOs in the contract surface or making every module in the panel reference the
    /// one door to the root process, which is what rules/security.md item 13 exists to narrow.
    /// </para>
    /// <para>
    /// <b>Ordering, and which way it fails.</b> The row is written last, so a database failure after
    /// a verified stop leaves the account really stopped while the panel still shows it active. That
    /// is the understating direction and the safe one; the reverse — a customer told their account is
    /// suspended while their sites keep serving — is precisely what this handler used to produce on
    /// every success, and the remark that used to sit here cited that outcome as the thing the
    /// ordering avoided while the code produced it unconditionally.
    /// </para>
    /// <para>
    /// <b>This operation can now fail where it never did.</b> Billing is the caller, and a refusal
    /// leaves the account exactly as it was — active, and suspendable again once whatever refused is
    /// fixed. A caller that treated the old unconditional success as a guarantee will see failures.
    /// </para>
    /// </remarks>
    /// <param name="command">Which account to suspend.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The account in its new state, or a typed failure.</returns>
    public async Task<Result<AccountDto>> HandleAsync(SuspendAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts.SingleOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);
        if (account is null)
        {
            // The subject is the identifier the caller supplied, because no name is known — a probe
            // for an account the caller may not see still leaves a trace naming what was probed for.
            // No TASK is opened on this path: nothing is running, so a task here would be a row an
            // operator can only read as the suspension of a raw identifier that never existed.
            return await FailAsync(
                command,
                command.AccountId.ToString(),
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                Guid.Empty,
                cancellationToken);
        }

        var taskId = await _tasks.BeginAsync(
            TaskKinds.AccountSuspension, account.Name, _correlationIds.CorrelationId, cancellationToken);

        try
        {
            await _tasks.ReportAsync(taskId, 10, "asking every module to stop what it runs", cancellationToken);
            await _bus.InvokeAsync(new AccountSuspending(account.Id, account.Name), cancellationToken);
        }
        catch (Exception exception)
        {
            // Deliberately broad: any subscriber, including a marketplace module this assembly was
            // never compiled knowing about, may refuse in any way it likes, and every one of those
            // ways has the same meaning here — the account must NOT be recorded as suspended.
            LogCascadeRefused(_logger, account.Name, exception);

            return await FailAsync(
                command,
                account.Name,
                Error.Of(nameof(ErrorMessages.AccountSuspensionFailed), ErrorType.Failure),
                taskId,
                cancellationToken);
        }

        await _tasks.ReportAsync(taskId, 50, "locking the account's own login", cancellationToken);

        var stopped = await _agent.SuspendAsync(account.Name, cancellationToken);
        if (!stopped.IsSuccess)
        {
            return await FailAsync(command, account.Name, stopped.Error!, taskId, cancellationToken);
        }

        // Nothing above throwing is not the same fact as the account having stopped, and for the
        // whole life of this handler it was not the same fact at all: there was no cascade, the
        // agent's two `usermod` calls returned success, and the panel wrote Suspended over websites
        // that kept serving. So the claim is CHECKED before it is made, and checked HERE — after
        // everything that acts and before the row that records it — because this is the last point
        // at which refusing still leaves the account intact and suspendable again.
        var observed = await _agent.GetSuspensionStateAsync(account.Name, cancellationToken);
        if (!observed.IsSuccess)
        {
            return await FailAsync(command, account.Name, observed.Error!, taskId, cancellationToken);
        }

        var unobserved = Unobserved(observed.Value!);
        if (unobserved is not null)
        {
            LogSuspensionUnobserved(_logger, account.Name, unobserved, null);

            return await FailAsync(
                command,
                account.Name,
                Error.Of(nameof(ErrorMessages.AccountSuspensionNotObserved), ErrorType.Failure),
                taskId,
                cancellationToken);
        }

        await _tasks.ReportAsync(taskId, 80, DescribeAttestation(observed.Value!), cancellationToken);

        account.Suspend();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.AccountSuspended, account.Name, command.IpAddress, command.UserAgent, cancellationToken);

        await _tasks.CompleteAsync(taskId, cancellationToken);

        return Result<AccountDto>.Ok(new AccountDto(
            account.Id, account.Name, account.PrimaryDomain, account.PlanId, account.Status, account.CreatedAt));
    }

    /// <summary>Why the host cannot be said to have stopped the account, or <c>null</c> when it can.</summary>
    /// <param name="state">What the host answered when asked what it is doing for the account.</param>
    /// <returns>Operator-facing English naming what was seen, or <c>null</c> when the stop is observed.</returns>
    /// <remarks>
    /// <para>
    /// Three conditions, and the first is the one that would otherwise make this check decoration.
    /// An unreadable vhost directory and an account with no vhost at all produce the same empty
    /// list, and the empty list is the one that reads as "everything is suspended" — so a blind
    /// answer must never be accepted as a suspended one. The other two are the facts themselves:
    /// every vhost the host holds is the suspended one, and the account's own login is locked.
    /// </para>
    /// <para>
    /// The site set is the HOST's and not the panel's, deliberately. A vhost the panel has forgotten
    /// creating is exactly the one still serving a suspended customer's page, so this refuses on
    /// vhosts no <c>Site</c> row names. That can only fail in the safe direction: it refuses a
    /// suspension the panel believes complete, and never certifies one.
    /// </para>
    /// </remarks>
    private static string? Unobserved(AccountSuspensionStateDto state)
    {
        if (!state.SitesDirectoryReadable)
        {
            return "the agent could not read the directory it lists vhosts from, so an empty answer "
                + "would mean 'it did not look' and not 'nothing is serving'";
        }

        var serving = state.Sites
            .Where(site => { return !site.ServingStub; })
            .Select(site => { return site.Domain; })
            .OrderBy(domain => { return domain; }, StringComparer.Ordinal)
            .ToList();

        if (serving.Count > 0)
        {
            return $"these vhosts are not the suspended one: {string.Join(", ", serving)}";
        }

        if (CanStillAuthenticate(state))
        {
            return "the account's own login can still be authenticated with a password";
        }

        // Counted out of the crontab itself, because this panel keeps no cron rows: the Cron module
        // owns no entity, so a check that consulted the database would be green over a firing
        // crontab. Foreign lines are NOT part of this condition — the panel does not silence them
        // and refusing on them would make an account with one hand-written cron line permanently
        // unsuspendable; they are reported on the task instead.
        if (state.CronEntriesSuspended != state.CronEntriesTotal)
        {
            var firing = state.CronEntriesTotal - state.CronEntriesSuspended;

            return $"{firing} of the account's {state.CronEntriesTotal} cron entries can still be run by cron";
        }

        // Enumerated from the host's password database and not from the Sftp module's rows: a login
        // the panel has forgotten is exactly the one still letting a suspended customer write to
        // their home, so this refuses on logins no row names. Like the vhost set, that can only fail
        // in the safe direction.
        var open = state.SftpLogins
            .Where(login => { return !login.Locked; })
            .Select(login => { return login.Username; })
            .OrderBy(username => { return username; }, StringComparer.Ordinal)
            .ToList();

        return open.Count > 0
            ? $"these sftp logins still authenticate: {string.Join(", ", open)}"
            : null;
    }

    /// <summary>Puts the attestation's own answer, and its standing blind spots, into one task line.</summary>
    /// <param name="state">What the host answered.</param>
    /// <returns>The line to report.</returns>
    /// <remarks>
    /// <para>
    /// This line is the only place the operator is told that anything was CHECKED, and the only
    /// place they are told what was not. A pane reading as three stages of doing and no stage of
    /// looking is how a completed task came to be believed over a site that was still serving.
    /// </para>
    /// <para>
    /// The qualification is not optional prose. What a suspension does not reach — the account's
    /// databases, the panel's own login, and any crontab line the panel did not write — is named on
    /// the line, because a completion that claimed more than it observed is the defect this whole
    /// attestation exists to end. The foreign-line count is the one of those the host can actually
    /// measure, so it is reported as a number rather than as a caveat.
    /// </para>
    /// <para>
    /// English and not localized, like every other line on a task: it is read by the operator who
    /// administers the server (rules/csharp.md).
    /// </para>
    /// </remarks>
    private static string DescribeAttestation(AccountSuspensionStateDto state)
    {
        var foreign = state.CronForeignLines == 0
            ? string.Empty
            : $", and {state.CronForeignLines} crontab line(s) the panel did not write, which it does "
                + "not touch and which keep firing";

        return $"the host shows the login locked, all {state.Sites.Count} of its vhosts for this account "
            + $"serving the suspended page, all {state.CronEntriesTotal} of its cron entries suppressed "
            + $"and all {state.SftpLogins.Count} of its sftp logins locked; NOT covered by this "
            + $"suspension: the account's databases and the panel's own web login{foreign}";
    }

    /// <summary>Journals a refused suspend and returns it as the typed failure.</summary>
    /// <param name="command">The suspend that was refused.</param>
    /// <param name="subject">The account's name, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="taskId">
    /// The task to close under the same code, or <see cref="Guid.Empty"/> on the path that opened
    /// none. Closing it in the one funnel every refusal passes through is what keeps the task and
    /// the response saying the same thing.
    /// </param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<AccountDto>> FailAsync(
        SuspendAccountCommand command,
        string subject,
        Error error,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.AccountSuspended, subject, command.IpAddress, command.UserAgent, cancellationToken);

        await _tasks.FailAsync(taskId, error.Code, cancellationToken);

        return Result<AccountDto>.Fail(error);
    }

    /// <summary>Whether a password could still authenticate the account's own login.</summary>
    /// <param name="state">What the host answered when asked what it is doing for the account.</param>
    /// <returns><c>true</c> when a suspension has not stopped the login.</returns>
    /// <remarks>
    /// <para>
    /// This is the question a SUSPENSION has to ask, and it is not the one reactivation asks. Two
    /// shadow states can authenticate — a usable hash, and an EMPTY field, which authenticates with
    /// the empty password — and both must refuse a suspension. Every other state cannot, whether it
    /// holds a hash behind a lock marker or no hash at all.
    /// </para>
    /// <para>
    /// <see cref="AccountLoginPasswordState.Unspecified"/> means the agent predates the field, so
    /// this falls back to <c>passwd -S</c>'s boolean — exactly the behaviour that existed before,
    /// which for this question is correct: <c>passwd -S</c> reports a login with no hash and a login
    /// locked over a hash as the same thing, and neither of them can authenticate. It is the OTHER
    /// direction, the one reactivation asks about, where collapsing them is fatal.
    /// </para>
    /// </remarks>
    private static bool CanStillAuthenticate(AccountSuspensionStateDto state)
    {
        return state.LoginPasswordState switch
        {
            AccountLoginPasswordState.Empty => true,
            AccountLoginPasswordState.Usable => true,
            AccountLoginPasswordState.Absent => false,
            AccountLoginPasswordState.Locked => false,
            _ => !state.LoginLocked,
        };
    }
}
