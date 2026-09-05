using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.Accounts.Commands.DeleteAccount;

/// <summary>Handles <see cref="DeleteAccountCommand"/> by removing the account from server and panel.</summary>
public sealed class DeleteAccountCommandHandler
{
    /// <summary>
    /// Pre-compiled log delegate for a module that refused to release what it holds. Source-generated
    /// because the reason belongs to the operator and never to the customer-facing message.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> LogCleanupRefused =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(DeleteAccountCommandHandler)),
            "Account {AccountName} was not deleted: a module refused to release what it holds.");

    /// <summary>
    /// Pre-compiled log delegate for a cascade that finished quietly and released nothing.
    /// </summary>
    /// <remarks>
    /// Error, not warning, and it names the entities: this is the failure that used to be invisible.
    /// The cascade did not throw, so every earlier version of this handler carried on and reported a
    /// completed deletion over rows that are still there. What an operator needs is the list.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogResidueFound =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(2, nameof(DeleteAccountCommandHandler)),
            "Account {AccountName} was not deleted: the cascade left rows behind — {Residue}.");

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The agent, which owns the system user and the home directory.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>The bus the cascade is announced on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where a subscriber's refusal is written for an operator to read.</summary>
    private readonly ILogger<DeleteAccountCommandHandler> _logger;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>The panel-wide task journal, so an operator can watch this run instead of waiting on it.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>What the composed panel still stores against the account once the cascade has run.</summary>
    private readonly IAccountResidueAuditor _residue;

    /// <summary>The current request's correlation id, recorded on the task beside its stages.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="agent">The agent client that removes the operating-system identity.</param>
    /// <param name="bus">The bus <see cref="AccountDeleting"/> is invoked on.</param>
    /// <param name="logger">Where a subscriber's refusal is recorded.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    /// <param name="residue">The audit of what the panel still stores against the account.</param>
    /// <param name="correlationIds">The current request's correlation id.</param>
    public DeleteAccountCommandHandler(
        AccountsDbContext dbContext,
        IAgentAccountsClient agent,
        IMessageBus bus,
        ILogger<DeleteAccountCommandHandler> logger,
        AccountAuditJournal journal,
        ITaskRecorder tasks,
        IAccountResidueAuditor residue,
        ICorrelationIdAccessor correlationIds)
    {
        _dbContext = dbContext;
        _agent = agent;
        _bus = bus;
        _logger = logger;
        _journal = journal;
        _tasks = tasks;
        _residue = residue;
        _correlationIds = correlationIds;
    }

    /// <summary>Removes the account, and everything any module holds against it.</summary>
    /// <remarks>
    /// <para>
    /// <b>Three steps, and the order is the whole of the safety.</b>
    /// </para>
    /// <para>
    /// 1. <see cref="AccountDeleting"/> is INVOKED — inline, not published — so every module holding
    /// rows against this account removes them first. It is invoked rather than published because a
    /// published message is handled later, by which time the account would already be gone and a
    /// subscriber's failure could no longer stop anything. <c>userdel</c> touches neither MySQL nor
    /// sshd, so before this cascade existed a deleted account left its database rows and its SFTP
    /// login rows behind — and system user names are recycled, so an account created again under
    /// the same name inherited them.
    /// </para>
    /// <para>
    /// 1a. The cascade's effect is AUDITED. A subscriber that does not exist cannot throw, so an
    /// unhandled event and a module with nothing to release look identical from here — which is
    /// exactly how a deletion came to report COMPLETED while two modules kept everything they held.
    /// The audit asks the composed panel's own mapping what still names this account, and a
    /// non-empty answer stops the deletion at the point where stopping is still free.
    /// </para>
    /// <para>
    /// 1b. <b>What COMPLETED is allowed to mean here.</b> Exactly this: the cascade was invoked and
    /// waited for, the audit found no row of this account in any module it could read, the host
    /// removal succeeded and the account row is gone. It does NOT mean the panel has proved the
    /// account left nothing anywhere — the audit reads this database and not the machine, and it
    /// skips a module it cannot read rather than making the account undeletable. Both limits are
    /// reported ON the task, in the stage line the audit writes, so the completion an operator sees
    /// is qualified by the same facts this comment is.
    /// </para>
    /// <para>
    /// 2. The agent removes what is on the HOST: the databases, the SFTP logins, the jail's bind
    /// mount, the php-fpm pools, and only then the system user. It asks the machine what is there
    /// rather than being handed this panel's list, which is why step 1 running first is safe — a row
    /// this panel has already forgotten is still found and removed by name.
    /// </para>
    /// <para>
    /// 3. The <c>Account</c> row goes last. Dropping it first would be the one unrecoverable order:
    /// the panel would forget an account whose Linux user, home directory and files are still on the
    /// server, with nothing left pointing at them.
    /// </para>
    /// <para>
    /// <b>A cleanup failure aborts the deletion.</b> Either half refusing leaves the account exactly
    /// as it was, which is the recoverable state — it can be deleted again once whatever refused is
    /// fixed. The alternative, carrying on, produces an orphan: a database or a live credential
    /// nothing in the panel points at, which no later operation can find. The exception is caught
    /// rather than allowed to escape so that the operator is answered with a typed failure instead
    /// of a 500, and it is LOGGED here because the subscriber's own text has no place in a
    /// customer-facing message (rules/security.md item 8).
    /// </para>
    /// <para>
    /// <b>Where the audit entry goes follows from that.</b> The SUCCESS entry is written last, after
    /// the row is gone, because only then is every one of the three steps known to have finished —
    /// and this cascade can fail after it has already destroyed things. A deletion that dropped the
    /// account's databases and its SFTP logins and then had the agent refuse must NOT read as
    /// "AccountDeleted, succeeded": an operator searching for why a customer's data vanished would
    /// find an entry claiming a clean removal of an account that is still there. That case takes a
    /// FAILURE entry instead, on the same action and the same account name, which is the honest
    /// record of what happened — the deletion was attempted, got part-way, and did not complete.
    /// The destruction the cascade did manage is not lost from the journal either: each module
    /// journals its own removals as it makes them, so the databases and logins have their own
    /// entries under their own actions.
    /// </para>
    /// </remarks>
    /// <param name="command">Which account to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>How many bytes the removal freed, or a typed failure.</returns>
    public async Task<Result<ulong>> HandleAsync(DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts.SingleOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);
        if (account is null)
        {
            // The subject is the identifier the caller supplied, because no name is known — a probe
            // for an account the caller may not see still leaves a trace naming what was probed for.
            // No TASK is opened on this path, and that is deliberate: nothing has been done and
            // nothing is running, so a task here would be a row an operator can only read as a
            // deletion of a raw identifier that never existed. The journal is where a probe belongs.
            return await FailAsync(
                command,
                command.AccountId.ToString(),
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                Guid.Empty,
                cancellationToken);
        }

        // From here on there IS an operation to watch, and it is the longest and most destructive
        // one the panel offers. The task carries the account's NAME, which is what an operator
        // searches for and the only thing that survives the deletion.
        var taskId = await _tasks.BeginAsync(
            TaskKinds.AccountDeletion, account.Name, _correlationIds.CorrelationId, cancellationToken);

        try
        {
            await _tasks.ReportAsync(taskId, 10, "asking every module to release what it holds", cancellationToken);
            await _bus.InvokeAsync(new AccountDeleting(account.Id, account.Name), cancellationToken);
        }
        catch (Exception exception)
        {
            // Deliberately broad: any subscriber, including a marketplace module this assembly was
            // never compiled knowing about, may refuse in any way it likes, and every one of those
            // ways has the same meaning here — the account must NOT be deleted.
            LogCleanupRefused(_logger, account.Name, exception);

            return await FailAsync(
                command, account.Name, Error.Of(nameof(ErrorMessages.AccountCleanupFailed), ErrorType.Failure), taskId, cancellationToken);
        }

        // The cascade not throwing is not the same fact as the cascade having done anything, and
        // for two modules it was not the same fact for a whole release: `Sites` and `Ssl` subscribed
        // to nothing, so the step above was silent, this handler carried on, and the task reported
        // COMPLETED at 100 over a `Site` row the panel then rendered as ENABLED for an account it no
        // longer had. So the claim is now CHECKED before it is made, and checked HERE — after the
        // cascade and before the agent — because this is the last point at which refusing still
        // leaves the account intact and deletable again.
        var residue = await _residue.FindResidueAsync(account.Id, cancellationToken);
        if (residue.Rows.Count > 0)
        {
            LogResidueFound(_logger, account.Name, string.Join(", ", residue.Rows), null);

            return await FailAsync(
                command, account.Name, Error.Of(nameof(ErrorMessages.AccountCleanupFailed), ErrorType.Failure), taskId, cancellationToken);
        }

        await _tasks.ReportAsync(taskId, 40, DescribeAudit(residue), cancellationToken);
        await _tasks.ReportAsync(taskId, 50, "removing the system user and its home directory", cancellationToken);

        var removed = await _agent.DeleteAsync(account.Name, cancellationToken);
        if (!removed.IsSuccess)
        {
            // The cascade above has already run, so this is the partial destruction the remarks
            // describe: journalled as a failure, never as a completed deletion.
            return await FailAsync(command, account.Name, removed.Error!, taskId, cancellationToken);
        }

        // Captured before the row goes: the journal records the name, which is the only thing about
        // a deleted account anybody will later be able to search for.
        var name = account.Name;

        await _tasks.ReportAsync(taskId, 90, "removing the panel's own record of the account", cancellationToken);

        _dbContext.Accounts.Remove(account);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.AccountDeleted, name, command.IpAddress, command.UserAgent, cancellationToken);

        await _tasks.CompleteAsync(taskId, cancellationToken);

        return Result<ulong>.Ok(removed.Value);
    }

    /// <summary>Puts the audit's own answer into one line of the operator's task log.</summary>
    /// <param name="residue">What the post-cascade audit saw, and what it could not see.</param>
    /// <returns>The line to report, qualified by whatever the audit could not read.</returns>
    /// <remarks>
    /// <para>
    /// This line is the only place the operator is told that anything was CHECKED. Without it the
    /// pane reads as three stages of doing and no stage of looking, which is how a task came to be
    /// believed over a site the panel was still rendering.
    /// </para>
    /// <para>
    /// The qualification matters more than the happy half. A module the audit could not read has not
    /// been found clean, and this deletion goes ahead anyway — deliberately, because an audit that
    /// could veto a deletion by failing would turn its own outage into an account nobody can remove.
    /// The honest consequence of proceeding is saying so: the modules that went unchecked are named
    /// on the task, so COMPLETED here means "the account is gone and these modules were asked",
    /// never "nothing of this account is left anywhere".
    /// </para>
    /// <para>
    /// English and not localized, like every other line on a task: it names entity and context types
    /// and is read by the operator who administers the server (rules/csharp.md).
    /// </para>
    /// </remarks>
    private static string DescribeAudit(AccountResidue residue)
    {
        const string Checked = "checking what the panel still holds: nothing names this account";

        return residue.Unchecked.Count == 0
            ? Checked
            : $"{Checked} in the modules that answered, but these could NOT be checked: {string.Join(", ", residue.Unchecked)}";
    }

    /// <summary>Journals a deletion that did not complete and returns it as the typed failure.</summary>
    /// <param name="command">The deletion that was refused.</param>
    /// <param name="subject">The account's name, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <param name="taskId">
    /// The task to close under the same code, or <see cref="Guid.Empty"/> on the paths that opened
    /// none. Closing it HERE, in the one funnel every refusal already passes through, is what makes
    /// the task and the response say the same thing: a code answered to the caller and not written
    /// onto the task would be a pane showing a deletion still running that had already failed.
    /// </param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<ulong>> FailAsync(
        DeleteAccountCommand command,
        string subject,
        Error error,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.AccountDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        await _tasks.FailAsync(taskId, error.Code, cancellationToken);

        return Result<ulong>.Fail(error);
    }
}
