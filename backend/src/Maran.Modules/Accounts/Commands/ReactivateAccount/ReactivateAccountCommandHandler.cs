using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Policies;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Localization;
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

    /// <summary>Refuses a write by a suspended reseller.</summary>
    private readonly ResellerScope _scope;

    /// <summary>Names the owning reseller in the answer.</summary>
    private readonly ResellerNameDirectory _resellerNames;

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

    /// <summary>Names the account's state in the response, in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="scope">Refuses a write by a suspended reseller.</param>
    /// <param name="resellerNames">Names the owning reseller in the answer.</param>
    /// <param name="agent">The agent client that performs the privileged half and answers the attestation.</param>
    /// <param name="bus">The bus <see cref="AccountResuming"/> is invoked on.</param>
    /// <param name="logger">Where a subscriber's refusal and an unobserved resumption are recorded.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    /// <param name="correlationIds">The current request's correlation id.</param>
    /// <param name="displayNames">Names the account's state in the response, in the request's culture.</param>
    public ReactivateAccountCommandHandler(
        AccountsDbContext dbContext,
        ResellerScope scope,
        ResellerNameDirectory resellerNames,
        IAgentAccountsClient agent,
        IMessageBus bus,
        ILogger<ReactivateAccountCommandHandler> logger,
        AccountAuditJournal journal,
        ITaskRecorder tasks,
        ICorrelationIdAccessor correlationIds,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _scope = scope;
        _resellerNames = resellerNames;
        _agent = agent;
        _bus = bus;
        _logger = logger;
        _journal = journal;
        _tasks = tasks;
        _correlationIds = correlationIds;
        _displayNames = displayNames;
    }

    /// <summary>
    /// Reactivates the account. Not short-circuited on the current status: reactivating an already
    /// active account runs the whole operation again (the module cascade, the agent unlock and its
    /// attestation, the journal entry and a new task) and ends in the same state.
    /// </summary>
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
    /// <c>&lt;account&gt;_*</c> transfer login — SFTP and FTPS alike — both driven by their own
    /// modules from the same event. Neither needs the selectivity sites need, and for the same
    /// reason: the suspension overwrote no customer state to begin with. Cron's suspension is a
    /// marker orthogonal to the per-entry
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
    /// marker — see <see cref="ResumptionAttestationPolicy"/> for why that is the question and not "is the login
    /// locked", which every hosting account answers yes to for ever — that no vhost it holds
    /// for this account is the suspended one, that no entry of its crontab still carries the
    /// suspension marker and that no transfer login of the account, under either daemon, is still
    /// locked. A resumption that cannot be observed is refused and the account stays suspended,
    /// which is the recoverable direction: it can be reactivated again once whatever refused is
    /// fixed, whereas a customer told their service is back over a stub is the lie in the other
    /// direction.
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
    /// <returns>
    /// The account in its new state, <c>ResellerSuspended</c> when the caller is a suspended reseller,
    /// or a typed failure. An account another reseller owns is <c>AccountNotFound</c>.
    /// </returns>
    public async Task<Result<AccountDto>> HandleAsync(
        ReactivateAccountCommand command,
        CancellationToken cancellationToken)
    {
        var inactive = await _scope.RequireActiveAsync(cancellationToken);
        if (inactive is not null)
        {
            return await FailAsync(command, command.AccountId.ToString(), inactive, Guid.Empty, cancellationToken);
        }

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

        // About this account, so an administrator finds it among the account's tasks; audience
        // AdministratorOnly, because it is an administrator's action ON the account, not work done
        // for its owner.
        var taskId = await _tasks.BeginAsync(
            TaskKinds.AccountResumption,
            account.Name,
            account.Id,
            TaskAudience.AdministratorOnly,
            _correlationIds.CorrelationId,
            cancellationToken);

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

        // The agent keeps no record of the shell an account was given and returns nologin on every
        // unsuspension unless told otherwise, so the shell the account was created with is stated here:
        // without it the first suspend and reactivate cycle would silently take the shell away.
        var started = await _agent.UnsuspendAsync(
            account.Name, AccountLoginShellMapper.ToAgent(account.ShellAccess), cancellationToken);
        if (!started.IsSuccess)
        {
            return await FailAsync(command, account.Name, started.Error!, taskId, cancellationToken);
        }

        var observed = await _agent.GetSuspensionStateAsync(account.Name, cancellationToken);
        if (!observed.IsSuccess)
        {
            return await FailAsync(command, account.Name, observed.Error!, taskId, cancellationToken);
        }

        var unobserved = ResumptionAttestationPolicy.StillStopped(observed.Value!);
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

        await _tasks.ReportAsync(taskId, 80, ResumptionAttestationPolicy.Describe(observed.Value!), cancellationToken);

        account.Reactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AccountAuditActions.AccountReactivated,
            account.Name,
            command.IpAddress,
            command.UserAgent,
            cancellationToken,
            account.Id);

        await _tasks.CompleteAsync(taskId, cancellationToken);

        return Result<AccountDto>.Ok(
            AccountMapper.ToDto(
                account,
                _displayNames,
                await _resellerNames.NameOfAsync(account.ResellerId, cancellationToken)));
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
            AccountAuditActions.AccountReactivated, subject, command.IpAddress, command.UserAgent, cancellationToken);

        await _tasks.FailAsync(taskId, error.Code, cancellationToken);

        return Result<AccountDto>.Fail(error);
    }
}
