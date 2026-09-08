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

namespace Maran.Modules.Accounts.Commands.ReactivateAccount;

/// <summary>Handles <see cref="ReactivateAccountCommand"/> by restarting the account on the server.</summary>
public sealed class ReactivateAccountCommandHandler
{
    /// <summary>
    /// Pre-compiled log delegate for a module that refused to restore what it had paused.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> LogCascadeRefused =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(ReactivateAccountCommandHandler)),
            "Account {AccountName} was not reactivated: a module refused to restore what it paused.");

    /// <summary>
    /// Pre-compiled log delegate for a host that cannot be observed to have restarted the account.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> LogResumptionUnobserved =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(2, nameof(ReactivateAccountCommandHandler)),
            "Account {AccountName} was not reactivated: the host does not show it running — {Observed}.");

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The agent, which owns everything outside the database.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>The bus the resumption cascade is announced on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where a subscriber's refusal and an unobserved resumption are written for an operator.</summary>
    private readonly ILogger<ReactivateAccountCommandHandler> _logger;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>The panel-wide task journal, so an operator can watch this run instead of waiting on it.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The current request's correlation id, recorded on the task beside its stages.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="agent">The agent client that performs the privileged half and answers the attestation.</param>
    /// <param name="bus">The bus <see cref="AccountResuming"/> is invoked on.</param>
    /// <param name="logger">Where a subscriber's refusal and an unobserved resumption are recorded.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    /// <param name="correlationIds">The current request's correlation id.</param>
    public ReactivateAccountCommandHandler(
        AccountsDbContext dbContext,
        IAgentAccountsClient agent,
        IMessageBus bus,
        ILogger<ReactivateAccountCommandHandler> logger,
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

    /// <summary>Reactivates the account. Idempotent: reactivating an active account changes nothing.</summary>
    /// <remarks>
    /// <para>
    /// <b>What is restored, and what is only restored SELECTIVELY.</b> The account's own Linux login
    /// is unlocked, and the Sites module — driven by <see cref="AccountResuming"/>, because this
    /// module may not reference it — puts back the vhost of every site whose <c>Site.Status</c> is
    /// <c>Enabled</c>. A site the customer had disabled themselves stays on the suspension page,
    /// which is the point: suspension stubbed every vhost and wrote nothing, so the panel's own row
    /// is the only surviving record of which sites the customer wanted serving, and restoring
    /// everything would silently overwrite that decision while claiming to undo the suspension.
    /// </para>
    /// <para>
    /// <b>What is restored exactly as it was.</b> Every entry in the account's crontab and every
    /// <c>&lt;account&gt;_*</c> SFTP login, both driven by their own modules from the same event.
    /// Neither needs the selectivity sites need, and for the same reason: the suspension overwrote
    /// no customer state to begin with. Cron's suspension is a marker orthogonal to the per-entry
    /// <c>enabled</c> flag, so clearing it gives back a job the customer had switched off still
    /// switched off; and locking a login prefixed its stored hash rather than replacing it, so
    /// unlocking restores the very password the customer was shown, not a new one.
    /// </para>
    /// <para>
    /// <b>What was never paused, so is not restored.</b> The account's databases, the panel's own web
    /// login, and any crontab line the panel did not write. Stated in full, with the reasons, on
    /// <see cref="SuspendAccount.SuspendAccountCommandHandler"/>.
    /// </para>
    /// <para>
    /// <b>What COMPLETED is allowed to mean here.</b> The cascade ran, the agent unlocked the login,
    /// and the HOST was then asked and answered that the login holds no password behind a lock
    /// marker — see <see cref="StillHeldDown"/> for why that is the question and not "is the login
    /// locked", which every hosting account answers yes to for ever — that no vhost it holds
    /// for this account is the suspended one, that no entry of its crontab still carries the
    /// suspension marker and that no SFTP login of the account is still locked. A resumption that cannot be observed is refused and the
    /// account stays suspended, which is the recoverable direction: it can be reactivated again once
    /// whatever refused is fixed, whereas a customer told their service is back over a stub is the
    /// lie in the other direction.
    /// </para>
    /// <para>
    /// <b>The attestation's asymmetry with suspension's, stated because it is not an oversight.</b>
    /// Suspension refuses unless EVERY vhost the host holds is the stub; resumption refuses if ANY of
    /// them still is. The sets are different — the host may hold a vhost for a site the customer had
    /// disabled, and that one is legitimately still the stub after a resumption — so the check here
    /// is that nothing this account serves is the stub, which is true exactly when the Sites handler
    /// restored the enabled sites and no other vhost was ever stubbed. The consequence, stated rather
    /// than hidden: an account whose every site the customer had disabled will fail this check, and
    /// the failure is the honest one — the panel cannot distinguish it from a resumption that did
    /// nothing, and refusing is the direction that leaves the operator able to look.
    /// </para>
    /// </remarks>
    /// <param name="command">Which account to reactivate.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The account in its new state, or a typed failure.</returns>
    public async Task<Result<AccountDto>> HandleAsync(
        ReactivateAccountCommand command,
        CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts.SingleOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);
        if (account is null)
        {
            // The subject is the identifier the caller supplied, because no name is known — a probe
            // for an account the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(
                command,
                command.AccountId.ToString(),
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                Guid.Empty,
                cancellationToken);
        }

        var taskId = await _tasks.BeginAsync(
            TaskKinds.AccountResumption, account.Name, _correlationIds.CorrelationId, cancellationToken);

        try
        {
            await _tasks.ReportAsync(
                taskId, 10, "asking every module to restore what the customer had enabled", cancellationToken);
            await _bus.InvokeAsync(new AccountResuming(account.Id, account.Name), cancellationToken);
        }
        catch (Exception exception)
        {
            LogCascadeRefused(_logger, account.Name, exception);

            return await FailAsync(
                command,
                account.Name,
                Error.Of(nameof(ErrorMessages.AccountResumptionFailed), ErrorType.Failure),
                taskId,
                cancellationToken);
        }

        await _tasks.ReportAsync(taskId, 50, "unlocking the account's own login", cancellationToken);

        var started = await _agent.UnsuspendAsync(account.Name, cancellationToken);
        if (!started.IsSuccess)
        {
            return await FailAsync(command, account.Name, started.Error!, taskId, cancellationToken);
        }

        var observed = await _agent.GetSuspensionStateAsync(account.Name, cancellationToken);
        if (!observed.IsSuccess)
        {
            return await FailAsync(command, account.Name, observed.Error!, taskId, cancellationToken);
        }

        var unobserved = StillStopped(observed.Value!);
        if (unobserved is not null)
        {
            LogResumptionUnobserved(_logger, account.Name, unobserved, null);

            return await FailAsync(
                command,
                account.Name,
                Error.Of(nameof(ErrorMessages.AccountResumptionNotObserved), ErrorType.Failure),
                taskId,
                cancellationToken);
        }

        await _tasks.ReportAsync(taskId, 80, DescribeAttestation(observed.Value!), cancellationToken);

        account.Reactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.AccountReactivated, account.Name, command.IpAddress, command.UserAgent, cancellationToken);

        await _tasks.CompleteAsync(taskId, cancellationToken);

        return Result<AccountDto>.Ok(new AccountDto(
            account.Id, account.Name, account.PrimaryDomain, account.PlanId, account.Status, account.CreatedAt));
    }

    /// <summary>Why the host cannot be said to have restarted the account, or <c>null</c> when it can.</summary>
    /// <param name="state">What the host answered when asked what it is doing for the account.</param>
    /// <returns>Operator-facing English naming what was seen, or <c>null</c> when the restart is observed.</returns>
    /// <remarks>
    /// The readable-directory condition is checked here for the same reason it is checked on the way
    /// in: an unreadable directory produces an empty site list, and an empty list would satisfy "no
    /// vhost is still the stub" by having looked at nothing. A blind answer is never an observation
    /// in either direction.
    /// </remarks>
    private static string? StillStopped(AccountSuspensionStateDto state)
    {
        if (!state.SitesDirectoryReadable)
        {
            return "the agent could not read the directory it lists vhosts from, so an empty answer "
                + "would mean 'it did not look' and not 'nothing is stubbed'";
        }

        var stubbed = state.Sites
            .Where(site => { return site.ServingStub; })
            .Select(site => { return site.Domain; })
            .OrderBy(domain => { return domain; }, StringComparer.Ordinal)
            .ToList();

        if (stubbed.Count > 0)
        {
            return $"these vhosts still serve the suspension page: {string.Join(", ", stubbed)}";
        }

        if (StillHeldDown(state))
        {
            return "the account's own login is still locked over a password that was not restored";
        }

        if (state.CronEntriesSuspended > 0)
        {
            return $"{state.CronEntriesSuspended} of the account's cron entries still carry the "
                + "suspension marker, so cron cannot run them";
        }

        // The one honest false refusal in this handler, and it is documented rather than papered
        // over: a login that never had a password cannot be unlocked — `usermod --unlock` refuses
        // that while exiting zero — so it reads locked here for ever. Every login the agent creates
        // is given a password in the same operation, so this can only be a hand-made login, and
        // refusing sends an operator to look at it instead of reporting a restoration that did not
        // happen.
        var locked = state.SftpLogins
            .Where(login => { return login.Locked; })
            .Select(login => { return login.Username; })
            .OrderBy(username => { return username; }, StringComparer.Ordinal)
            .ToList();

        return locked.Count > 0
            ? $"these sftp logins are still locked: {string.Join(", ", locked)}"
            : null;
    }

    /// <summary>Puts the attestation's own answer, and its standing blind spots, into one task line.</summary>
    /// <param name="state">What the host answered.</param>
    /// <returns>The line to report.</returns>
    /// <remarks>
    /// It names what was NOT covered for the same reason suspension's does: the account's databases
    /// and the panel's own web login were never stopped, so a resumption reporting an unqualified
    /// restart would imply they had been. English and not localized, like every other task line
    /// (rules/csharp.md).
    /// </remarks>
    private static string DescribeAttestation(AccountSuspensionStateDto state)
    {
        return $"the host shows the login unlocked, none of its {state.Sites.Count} vhosts for this "
            + $"account serving the suspension page, all {state.CronEntriesTotal} of its cron entries "
            + $"free to run again and all {state.SftpLogins.Count} of its sftp logins unlocked; the "
            + "account's databases and the panel's own web login were never stopped by the "
            + "suspension, so nothing here restored them";
    }

    /// <summary>Journals a refused reactivate and returns it as the typed failure.</summary>
    /// <param name="command">The reactivate that was refused.</param>
    /// <param name="subject">The account's name, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="taskId">The task to close under the same code, or <see cref="Guid.Empty"/>.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<AccountDto>> FailAsync(
        ReactivateAccountCommand command,
        string subject,
        Error error,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.AccountReactivated, subject, command.IpAddress, command.UserAgent, cancellationToken);

        await _tasks.FailAsync(taskId, error.Code, cancellationToken);

        return Result<AccountDto>.Fail(error);
    }

    /// <summary>Whether something of the customer's is still held down on the account's own login.</summary>
    /// <param name="state">What the host answered when asked what it is doing for the account.</param>
    /// <returns><c>true</c> when the login carries a password the resumption failed to give back.</returns>
    /// <remarks>
    /// <para>
    /// This is the question a REACTIVATION has to ask, and until this method existed it was answered
    /// with the SUSPENSION question's answer. <c>passwd -S</c> — the source of
    /// <see cref="AccountSuspensionStateDto.LoginLocked"/> — reports a login locked over a password
    /// and a login that never had one as the same thing, and every hosting account is the second
    /// kind, because the agent sets no password on an account's own entry. So the boolean was true
    /// for such an account for ever and this handler refused EVERY hosting account, on both
    /// families, even where the agent had unlocked exactly what there was to unlock.
    /// </para>
    /// <para>
    /// Only <see cref="AccountLoginPasswordState.Locked"/> is a real hold: a hash behind a lock
    /// marker, and the one state in which the agent's <c>usermod --unlock</c> has work to do, so
    /// seeing it after a resumption means the unlock did not happen.
    /// <see cref="AccountLoginPasswordState.Absent"/> is what a NEVER-SUSPENDED account looks like —
    /// there is nothing to give back and nothing was taken — and
    /// <see cref="AccountLoginPasswordState.Usable"/> is a login that authenticates today. Neither
    /// is a reason to refuse. <see cref="AccountLoginPasswordState.Empty"/> is not held down either;
    /// it is a login with no password at all, which suspension refuses to create and which this
    /// handler has no power to repair — refusing here would leave such an account permanently
    /// unreactivatable while doing nothing about the open door.
    /// </para>
    /// <para>
    /// <see cref="AccountLoginPasswordState.Unspecified"/> means the agent predates the field. It
    /// falls back to the boolean, which restores the OLD refusal — the defect — rather than
    /// inventing a permission from an answer nobody gave. A skewed pair is then no worse off than
    /// before and never more permissive; the handshake covers one release of skew (rules/proto.md).
    /// </para>
    /// </remarks>
    private static bool StillHeldDown(AccountSuspensionStateDto state)
    {
        return state.LoginPasswordState switch
        {
            AccountLoginPasswordState.Locked => true,
            AccountLoginPasswordState.Absent => false,
            AccountLoginPasswordState.Empty => false,
            AccountLoginPasswordState.Usable => false,
            _ => state.LoginLocked,
        };
    }
}
