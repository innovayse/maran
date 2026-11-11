using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Mail.Services;

/// <summary>
/// Binds the mail an account submits LOCALLY (sendmail, PHP <c>mail()</c>, cron) to the account: reads the
/// account's domains, mailboxes and plan and sends the agent the complete binding, or removes it
/// (issue #518, contract <c>docs/mail/local-submission.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the whole binding, every time.</b> The agent's call REPLACES the account's domains and mailbox
/// logins, so a call carrying only the new domain would erase every other one. The sets are therefore always
/// rebuilt from this module's rows, never from what the triggering request mentions, and a repeated call
/// converges.
/// </para>
/// <para>
/// <b>The rows are read as the SYSTEM.</b> The binding is a system action, not the caller's own data: a
/// reseller, an API key or an unattended job that changes an account is answered NO rows by this module's
/// tenant filter, and a binding built from them would be empty and would REPLACE the real one with nothing.
/// The reads therefore ignore the tenant filter and are keyed on the account's id alone. The figures are sent
/// to the agent and never returned to the caller.
/// </para>
/// <para>
/// <b>The send limit is the ACCOUNT's, not a mailbox's.</b> One hourly bucket counts local submission and
/// every authenticated mailbox of the account together, and its size is the plan's
/// <see cref="MailLimitsSnapshot.MailboxSendLimitPerHour"/>. A figure the agent refuses (zero is never read
/// as "no limit") comes back as the agent's typed failure.
/// </para>
/// <para>
/// <b>The ledger.</b> The agent can set and remove a binding but cannot list what it holds, so the panel keeps
/// <see cref="MailSubmissionBinding"/> rows (one per system user) saying whether the agent's binding is
/// current, out of date, withdrawn, or owed a removal. It is what lets <see cref="ReconcileAsync"/> converge a
/// push that failed after a committed change and remove the binding of an account that was deleted or
/// suspended while mail was off. A removal the agent answers with NotFound (already unbound) is a success.
/// </para>
/// <para>
/// <b>One turn per user.</b> Pushes, removals and their ledger rows take turns per system user
/// (<see cref="MailSubmissionGate"/>). A change that is pushed after a suspension finds the ledger saying
/// "withdrawn" and pushes nothing, so a suspended account is never bound again by a racing domain change; only
/// an explicit <see cref="PublishAsync"/> (creation, resumption) binds a withdrawn account.
/// </para>
/// <para>
/// <b>Two failure modes.</b> <see cref="PublishAsync"/> and <see cref="RemoveAsync"/> return the failure, for
/// the callers that abort on it (suspension, resumption, deletion). <see cref="TryPublishAfterChangeAsync(AccountSnapshot, CancellationToken)"/>
/// is for a change that is already committed (a domain or mailbox row, a plan): the customer cannot undo it, so
/// it records the account as out of date and logs the agent's error CODE; <see cref="ReconcileAsync"/>, run on
/// a schedule, pushes it again.
/// </para>
/// </remarks>
public sealed class MailLocalSubmissionPublisher
{
    /// <summary>Pre-compiled log delegate for a binding that could not be pushed.</summary>
    /// <remarks>Names the account's system user and the agent's error code, never the agent's text or an address.</remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogPublishFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(MailLocalSubmissionPublisher)),
            "The local submission binding of account {AccountUsername} could not be updated ({AgentErrorCode}); "
            + "it is recorded as out of date and the reconciler pushes it again.");

    /// <summary>Pre-compiled log delegate for a binding that could not be removed by the reconciler.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogRemoveFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2, nameof(MailLocalSubmissionPublisher)),
            "The local submission binding of {AccountUsername} could not be removed ({AgentErrorCode}); "
            + "the removal stays owed and the reconciler tries again.");

    /// <summary>The Mail module's context, whose rows are the binding's source and whose ledger records it.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Tells whether mail is turned on at all.</summary>
    private readonly MailServerReader _server;

    /// <summary>The agent, which owns the host's filter data.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>The one window onto an account's system user name, state and plan.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Makes the changes to one user's binding take turns.</summary>
    private readonly MailSubmissionGate _gate;

    /// <summary>Where a failed push or removal is reported.</summary>
    private readonly ILogger<MailLocalSubmissionPublisher> _logger;

    /// <summary>Creates the publisher.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="server">Reads whether an administrator turned mail on.</param>
    /// <param name="agent">The agent client that holds the host's binding.</param>
    /// <param name="accounts">The window onto the Accounts module.</param>
    /// <param name="gate">Makes the changes to one user's binding take turns.</param>
    /// <param name="logger">Where a failed push or removal is reported.</param>
    public MailLocalSubmissionPublisher(
        MailDbContext dbContext,
        MailServerReader server,
        IAgentMailClient agent,
        IAccountDirectory accounts,
        MailSubmissionGate gate,
        ILogger<MailLocalSubmissionPublisher> logger)
    {
        _dbContext = dbContext;
        _server = server;
        _agent = agent;
        _accounts = accounts;
        _gate = gate;
        _logger = logger;
    }

    /// <summary>Sends the agent the account's complete binding, built from the current rows and plan.</summary>
    /// <param name="account">The account, with its system user name, state and plan.</param>
    /// <param name="cancellationToken">Cancels the read and the call.</param>
    /// <returns>Success — also when mail is off and nothing was sent — or the agent's typed failure.</returns>
    /// <remarks>
    /// Sends whatever the account's state and ledger say: the resumption pushes the binding while the account
    /// still reads as suspended, because the event precedes the state change. A failure leaves the ledger
    /// saying the binding is out of date. With mail off nothing is sent and an existing ledger row is marked
    /// out of date, so enabling mail pushes it.
    /// </remarks>
    public async Task<Result> PublishAsync(AccountSnapshot account, CancellationToken cancellationToken)
    {
        using var turn = await _gate.EnterAsync(account.Username, cancellationToken);

        return await PushAsync(account, cancellationToken);
    }

    /// <summary>
    /// Reads the account as the SYSTEM and sends the agent its complete binding, whatever the caller's tenant
    /// scope shows.
    /// </summary>
    /// <param name="accountId">The account whose binding is pushed; the caller holds the id from an event, never from a request.</param>
    /// <param name="cancellationToken">Cancels the read and the call.</param>
    /// <returns>Success, <c>AccountNotFound</c> when no such account exists, or the agent's typed failure.</returns>
    /// <remarks>
    /// The binding is a system action, not the caller's own data: a reseller who resumes a customer's account
    /// is answered nothing by the tenant-scoped <see cref="IAccountDirectory.FindAsync"/>, and the resumption
    /// must not abort for that. <see cref="IAccountDirectory.FindForSystemAsync"/> reads the one account whose
    /// id the event named; its figures are never returned to the caller. Every caller runs after the owning
    /// module authorised the action that raised the event.
    /// </remarks>
    public async Task<Result> PublishAsSystemAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await FindAsSystemAsync(accountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        return await PublishAsync(account, cancellationToken);
    }

    /// <summary>
    /// Reads the account as the SYSTEM and pushes its binding after a committed change, recording and logging
    /// a failure instead of returning it.
    /// </summary>
    /// <param name="accountId">The account whose plan or rows changed.</param>
    /// <param name="cancellationToken">Cancels the read and the call.</param>
    /// <returns>A task that completes when the push was made, skipped or its failure recorded.</returns>
    /// <remarks>An account that no longer exists is skipped; a suspended one is skipped as in <see cref="TryPublishAfterChangeAsync(AccountSnapshot, CancellationToken)"/>.</remarks>
    public async Task TryPublishAfterChangeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await FindAsSystemAsync(accountId, cancellationToken);
        if (account is not null)
        {
            await TryPublishAfterChangeAsync(account, cancellationToken);
        }
    }

    /// <summary>Pushes the binding after a change that is already committed, recording and logging a failure instead of returning it.</summary>
    /// <param name="account">The account whose rows or plan changed.</param>
    /// <param name="cancellationToken">Cancels the read and the call.</param>
    /// <returns>A task that completes when the push was made, skipped or its failure recorded.</returns>
    /// <remarks>
    /// A suspended account is skipped, and so is one whose ledger row says its binding was withdrawn or is
    /// owed a removal. The ledger is read inside the user's turn, after any suspension that started earlier
    /// has finished, so a change racing a suspension cannot bind the account again. The account stays
    /// unbound until the resumption pushes its binding.
    /// </remarks>
    public async Task TryPublishAfterChangeAsync(AccountSnapshot account, CancellationToken cancellationToken)
    {
        if (account.State == AccountState.Suspended)
        {
            return;
        }

        using var turn = await _gate.EnterAsync(account.Username, cancellationToken);
        var row = await FindRowAsync(account.Username, cancellationToken);
        if (row is not null && IsWithdrawn(row.State))
        {
            return;
        }

        var published = await PushAsync(account, cancellationToken);
        if (!published.IsSuccess)
        {
            LogPublishFailed(_logger, account.Username, published.Error!.Code, null);
        }
    }

    /// <summary>Removes the account's binding, so its system user can no longer submit mail locally.</summary>
    /// <param name="accountUsername">System username of the account.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success — also when the agent holds no binding for the user, or mail is off — or the agent's typed failure.</returns>
    /// <remarks>
    /// The agent answering NotFound means the user is already unbound, which is what was asked for. With mail
    /// off nothing is sent, but a ledger row that says the agent may hold a binding is marked as owed a
    /// removal, so the reconciler removes it once mail is on; the maps are not cleared by turning mail off.
    /// A failure leaves the removal owed.
    /// </remarks>
    public async Task<Result> RemoveAsync(string accountUsername, CancellationToken cancellationToken)
    {
        using var turn = await _gate.EnterAsync(accountUsername, cancellationToken);

        return await UnbindAsync(accountUsername, cancellationToken);
    }

    /// <summary>
    /// Brings the agent's bindings in line with the accounts that exist: pushes the ones that are owed, and
    /// removes the ones that no longer belong.
    /// </summary>
    /// <param name="pushEveryAccount">
    /// True to push every account that is not suspended, whatever the ledger says (enabling mail, which can
    /// leave the host's maps empty); false to push only the accounts the ledger says are owed a push.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads and the calls.</param>
    /// <returns>A task that completes when every account was reconciled or its failure recorded.</returns>
    /// <remarks>
    /// <para>
    /// An account that is not suspended is pushed when it has no ledger row (created while mail was off or
    /// before its push succeeded), its row is out of date, withdrawn, or owed a removal that did not happen (a
    /// refused suspension leaves the account active), or <paramref name="pushEveryAccount"/> is set. A
    /// suspended account whose row says the agent may hold a binding is removed. A ledger row whose user
    /// belongs to no account (deleted while mail was off, or whose removal failed) is removed from the agent
    /// and then forgotten. One account's failure is logged by code and does not stop the others.
    /// </para>
    /// <para>
    /// Reads through the unscoped <see cref="IAccountDirectory.ListAsync"/>, so it MUST be called only from an
    /// administrator-gated path or from unattended work with no request: its callers are the enabling of the
    /// mail server and the scheduled reconcile job.
    /// </para>
    /// </remarks>
    public async Task ReconcileAsync(bool pushEveryAccount, CancellationToken cancellationToken)
    {
        if (await _server.FindEnabledAsync(cancellationToken) is null)
        {
            return;
        }

        var accounts = await _accounts.ListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            await ReconcileAccountAsync(account, pushEveryAccount, cancellationToken);
        }

        var known = accounts
            .Select(account =>
            {
                return account.Username;
            })
            .ToHashSet(StringComparer.Ordinal);
        var rows = await _dbContext.MailSubmissionBindings
            .AsNoTracking()
            .Select(row => row.Username)
            .ToListAsync(cancellationToken);
        foreach (var username in rows)
        {
            if (!known.Contains(username))
            {
                await ForgetAsync(username, cancellationToken);
            }
        }
    }

    /// <summary>Reconciles one account that exists.</summary>
    /// <param name="account">The account.</param>
    /// <param name="pushEveryAccount">Whether a bound account is pushed again.</param>
    /// <param name="cancellationToken">Cancels the reads and the call.</param>
    /// <returns>A task that completes when the account was reconciled or its failure recorded.</returns>
    private async Task ReconcileAccountAsync(
        AccountSnapshot account,
        bool pushEveryAccount,
        CancellationToken cancellationToken)
    {
        using var turn = await _gate.EnterAsync(account.Username, cancellationToken);
        var row = await FindRowAsync(account.Username, cancellationToken);

        if (account.State == AccountState.Suspended)
        {
            if (row is not null && row.State != MailSubmissionBindingState.Withdrawn)
            {
                await RemoveAndLogAsync(account.Username, cancellationToken);
            }

            return;
        }

        if (row is null || row.State != MailSubmissionBindingState.Bound || pushEveryAccount)
        {
            var pushed = await PushAsync(account, cancellationToken);
            if (!pushed.IsSuccess)
            {
                LogPublishFailed(_logger, account.Username, pushed.Error!.Code, null);
            }
        }
    }

    /// <summary>Removes the binding of a user that belongs to no account, then forgets the ledger row.</summary>
    /// <param name="username">System username with a ledger row and no account.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the row was forgotten or the failure logged.</returns>
    private async Task ForgetAsync(string username, CancellationToken cancellationToken)
    {
        using var turn = await _gate.EnterAsync(username, cancellationToken);
        var row = await FindRowAsync(username, cancellationToken);
        if (row is null)
        {
            return;
        }

        if (row.State != MailSubmissionBindingState.Withdrawn)
        {
            var removed = await UnbindAsync(username, cancellationToken);
            if (!removed.IsSuccess)
            {
                LogRemoveFailed(_logger, username, removed.Error!.Code, null);

                return;
            }
        }

        _dbContext.MailSubmissionBindings.Remove(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Removes one user's binding and logs a refusal. The caller holds the user's turn.</summary>
    /// <param name="username">System username of the account.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the removal was made or its failure logged.</returns>
    private async Task RemoveAndLogAsync(string username, CancellationToken cancellationToken)
    {
        var removed = await UnbindAsync(username, cancellationToken);
        if (!removed.IsSuccess)
        {
            LogRemoveFailed(_logger, username, removed.Error!.Code, null);
        }
    }

    /// <summary>Builds the account's binding from the current rows, sends it and records the outcome. The caller holds the user's turn.</summary>
    /// <param name="account">The account, with its system user name and plan.</param>
    /// <param name="cancellationToken">Cancels the read and the call.</param>
    /// <returns>Success, or the agent's typed failure.</returns>
    private async Task<Result> PushAsync(AccountSnapshot account, CancellationToken cancellationToken)
    {
        if (await _server.FindEnabledAsync(cancellationToken) is null)
        {
            // The agent keeps what it holds; the push it is owed happens when mail is enabled.
            await RecordIfKnownAsync(account.Username, MailSubmissionBindingState.OutOfDate, cancellationToken);

            return Result.Ok();
        }

        // The binding is a system action, so these reads ignore the tenant filter: a reseller, an API key or an
        // unattended job is shown no rows by it, and an empty binding would REPLACE the account's real one. The
        // AccountId predicate is the whole scope, and "this account's domains" never becomes "every account's".
#pragma warning disable RS0030 // the binding is built for the account whoever asked for the change that triggered it
        var domains = await _dbContext.MailDomains
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(row => row.AccountId == account.Id)
            .OrderBy(row => row.Name)
            .Select(row => row.Name)
            .ToListAsync(cancellationToken);
        var logins = await _dbContext.Mailboxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(row => row.AccountId == account.Id)
            .Select(row => new { row.LocalPart, DomainName = row.MailDomain.Name })
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        var mailboxLogins = logins
            .Select(row =>
            {
                return MailboxAddressPolicy.Compose(row.LocalPart, row.DomainName);
            })
            .Order(StringComparer.Ordinal)
            .ToList();

        // A non-positive plan figure is sent as zero so the agent refuses it, never read as "no limit".
        var limit = (uint)Math.Max(0, account.MailLimits.MailboxSendLimitPerHour);

        var sent = await _agent.SetLocalSubmissionAsync(
            new AgentLocalSubmission(account.Username, domains, limit, mailboxLogins),
            cancellationToken);

        await RecordAsync(
            account.Username,
            sent.IsSuccess ? MailSubmissionBindingState.Bound : MailSubmissionBindingState.OutOfDate,
            cancellationToken);

        return sent;
    }

    /// <summary>Sends the removal and records the outcome. The caller holds the user's turn.</summary>
    /// <param name="username">System username of the account.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success, or the agent's typed failure.</returns>
    private async Task<Result> UnbindAsync(string username, CancellationToken cancellationToken)
    {
        if (await _server.FindEnabledAsync(cancellationToken) is null)
        {
            // The maps survive turning mail off, so whatever the agent holds stays owed a removal.
            var row = await FindRowAsync(username, cancellationToken);
            if (row is not null && row.State != MailSubmissionBindingState.Withdrawn)
            {
                await RecordAsync(username, MailSubmissionBindingState.WithdrawalPending, cancellationToken);
            }

            return Result.Ok();
        }

        var removed = await _agent.RemoveLocalSubmissionAsync(username, cancellationToken);

        // NotFound: the user holds no binding, which is the state that was asked for.
        var unbound = removed.IsSuccess || removed.Error!.Type == ErrorType.NotFound;
        await RecordAsync(
            username,
            unbound ? MailSubmissionBindingState.Withdrawn : MailSubmissionBindingState.WithdrawalPending,
            cancellationToken);

        return unbound ? Result.Ok() : removed;
    }

    /// <summary>True for the states in which no binding is wanted for the user.</summary>
    /// <param name="state">The ledger state.</param>
    /// <returns>Whether a change must leave the user unbound.</returns>
    private static bool IsWithdrawn(MailSubmissionBindingState state)
    {
        return state is MailSubmissionBindingState.Withdrawn or MailSubmissionBindingState.WithdrawalPending;
    }

    /// <summary>Reads one ledger row, tracked so a later record updates it.</summary>
    /// <param name="username">System username of the account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row, or <c>null</c> when the panel never recorded a binding for the user.</returns>
    private async Task<MailSubmissionBinding?> FindRowAsync(string username, CancellationToken cancellationToken)
    {
        return await _dbContext.MailSubmissionBindings.SingleOrDefaultAsync(
            row => row.Username == username,
            cancellationToken);
    }

    /// <summary>Records what is now known about a user's binding, creating the row when there is none.</summary>
    /// <param name="username">System username of the account.</param>
    /// <param name="state">The state to record.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row is saved.</returns>
    private async Task RecordAsync(string username, MailSubmissionBindingState state, CancellationToken cancellationToken)
    {
        var row = await FindRowAsync(username, cancellationToken);
        if (row is null)
        {
            _dbContext.MailSubmissionBindings.Add(new MailSubmissionBinding(username, state));
        }
        else
        {
            row.MarkAs(state);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Records a state only for a user the ledger already knows.</summary>
    /// <param name="username">System username of the account.</param>
    /// <param name="state">The state to record.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row, if any, is saved.</returns>
    private async Task RecordIfKnownAsync(string username, MailSubmissionBindingState state, CancellationToken cancellationToken)
    {
        if (await FindRowAsync(username, cancellationToken) is not null)
        {
            await RecordAsync(username, state, cancellationToken);
        }
    }

    /// <summary>Finds one account by id without the caller's tenant scope.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot, or <c>null</c> when no such account exists.</returns>
    private async Task<AccountSnapshot?> FindAsSystemAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await _accounts.FindForSystemAsync(accountId, cancellationToken);
    }
}
