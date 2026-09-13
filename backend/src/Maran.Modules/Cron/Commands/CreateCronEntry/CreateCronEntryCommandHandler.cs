using Maran.Agent.Client.Interfaces;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Commands.CreateCronEntry;

/// <summary>
/// Handles <see cref="CreateCronEntryCommand"/>: resolves the account, refuses an entry the plan
/// does not allow, and installs the rest through the agent.
/// </summary>
/// <remarks>
/// <para>
/// <b>The plan limit is counted against the AGENT'S listing, not against a panel table, because
/// there is no panel table and there must not be one.</b> The crontab is the record: the account
/// owns it and can add entries directly over SFTP, so a count of rows the panel had installed would
/// be a count of part of the crontab, and a customer at their limit could pass it by editing their
/// own crontab. Counting what the server actually holds makes the limit true rather than nearly
/// true — and it costs one extra agent call before every creation, which is the price of not
/// keeping a second copy of somebody else's data.
/// </para>
/// <para>
/// Order: account first, listing second, creation third. Spec §8 requires countable limits to be
/// enforced before the agent is asked to make anything, and here the listing is itself an agent
/// call — so the ordering that matters is that nothing is INSTALLED until the count has been read.
/// A listing that fails refuses the creation rather than being read as "zero entries so far", which
/// would turn an agent outage into an unlimited plan.
/// </para>
/// <para>
/// <b>THE RACE, and why the second check is not a duplicate of the first.</b> The count above and
/// the creation below are two separate agent calls, so two requests interleave between them and both
/// read N. Sites, Databases, Sftp and Ftp close that window with a per-account advisory lock taken
/// inside the panel's own transaction — count, insert, commit — and this module cannot: it keeps no
/// rows, so there is no table to count, no <c>DbContext</c> to lock, and no insert to be the atomic
/// act. A panel lock held across the agent round trip is what those gates refuse to do, and
/// inventing it here would be a second, worse shape.
/// </para>
/// <para>
/// So the allowance travels with the creation. <see cref="IAgentCronClient.CreateEntryAsync"/> carries
/// <c>maxEntries</c>, and the agent refuses inside the per-account cron lock it already holds across
/// the read of the crontab and the install of the new table — the one place in the system where this
/// count and this write are one indivisible act. A creation that loses the race arrives back as
/// <c>CronEntryLimitReachedConcurrently</c>, a DIFFERENT code from the refusal below, so an operator
/// can tell a plan that is simply full from a race that was lost.
/// </para>
/// <para>
/// The pre-check below therefore STAYS, and the two are not one check written twice. They answer
/// different questions and neither can answer the other's. This one refuses the ordinary full-plan
/// request without touching the host at all — no root process, no privileged work — and it is where
/// the plan lives, since the agent holds no plan and never will (rules/architecture.md: the agent
/// MUST stay stateless). The agent's one closes a window measured in the length of a round trip,
/// which is the only thing the panel cannot close from here. It is the same relationship every other
/// agent input already has: validated at the boundary, re-checked inside the agent
/// (rules/security.md item 1), and enforcement authority still on the backend, because the number the
/// agent compares against is the number this handler sent it.
/// </para>
/// <para>
/// What neither check covers, and no lock could: the account owns its crontab and can add entries
/// over SFTP. The limit is true about what the panel installs and about what the host held when the
/// agent last looked; it is not a quota the kernel enforces.
/// </para>
/// <para>
/// Every failure is journalled and none of them carries the command (RULING 31): the subject is the
/// new entry's id on success and the ACCOUNT's id on refusal, because before the agent answers
/// there is no entry to name. See <see cref="CronAuditJournal"/>.
/// </para>
/// </remarks>
public sealed class CreateCronEntryCommandHandler
{
    /// <summary>The one window onto the owning account's system user name and plan allowance.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the crontab — this module's only store.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly CronAuditJournal _journal;

    /// <summary>Where an agent refusal leaves its code and its subject, and nothing else.</summary>
    private readonly ILogger<CreateCronEntryCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name and plan allowance.</param>
    /// <param name="agent">The agent client that reads and writes the crontab.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and entry id only.</param>
    public CreateCronEntryCommandHandler(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        CronAuditJournal journal,
        ILogger<CreateCronEntryCommandHandler> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Installs the entry, refusing it before the crontab is touched when anything says no.</summary>
    /// <param name="command">The validated parameters; see <see cref="CreateCronEntryCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The installed entry — or <c>AccountNotFound</c>, <c>CronEntryLimitReached</c> when the plan is
    /// already full before the host is touched, <c>CronEntryLimitReachedConcurrently</c> when it
    /// became full between the count and the install and the agent refused under its own lock,
    /// <c>CronEntryAlreadyExists</c> when the agent already holds this exact schedule and command, or
    /// <c>CronOperationFailed</c>.
    /// </returns>
    public async Task<Result<CronEntryDto>> HandleAsync(
        CreateCronEntryCommand command,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped: the directory answers null for an account this caller does not own, so a
        // guessed account id reads as "not found" rather than "forbidden". This resolution is the
        // whole of the tenant boundary in a module that keeps no rows.
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var installed = await _agent.ListEntriesAsync(account.Username, cancellationToken);
        if (!installed.IsSuccess)
        {
            return await FailAsync(
                command,
                CronAgentErrorTranslator.Translate(
                    _logger, installed.Error!, nameof(_agent.ListEntriesAsync), Subject(command)),
                cancellationToken);
        }

        if (installed.Value.Count >= account.MaxCronEntries)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.CronEntryLimitReached), ErrorType.Conflict), cancellationToken);
        }

        // The allowance goes WITH the creation, and this is the only enforcement of it that cannot be
        // stale: the agent compares it against a count it takes under the per-account cron lock it
        // holds across the install. The check above has already refused the ordinary full plan
        // without touching the host; what is left for this to catch is another request that got
        // between the two.
        var created = await _agent.CreateEntryAsync(
            account.Username,
            CronScheduleTranslator.ToAgentSchedule(command.Schedule),
            command.Command,
            AllowanceOf(account),
            cancellationToken);
        if (!created.IsSuccess)
        {
            return await FailAsync(
                command,
                CronAgentErrorTranslator.Translate(
                    _logger, created.Error!, nameof(_agent.CreateEntryAsync), Subject(command)),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.CronEntryCreated, created.Value, command.IpAddress, command.UserAgent, cancellationToken);

        // Enabled, because the agent installs a new entry as a live crontab line; disabling is a
        // separate operation and a separate audit action.
        return Result<CronEntryDto>.Ok(new CronEntryDto(
            created.Value, command.AccountId, command.Schedule, command.Command, Enabled: true));
    }

    /// <summary>The account's cron entry allowance, as the agent's contract states one.</summary>
    /// <param name="account">The owning account, carrying its plan's allowance.</param>
    /// <returns>The allowance, clamped at zero.</returns>
    /// <remarks>
    /// Always a stated allowance and never <c>null</c>: this panel knows the number, so withholding
    /// it would leave the agent enforcing nothing and the window this whole arrangement exists to
    /// close still open. <c>null</c> is on the contract for a caller that does NOT know — an older
    /// panel, or any other client — and that is the case the agent's optional field is shaped for,
    /// not this one.
    ///
    /// A negative plan value cannot come from the database (the column is constrained) but the CLR
    /// type is <c>int</c>, so the conversion states what it does with one rather than throwing inside
    /// a request: it becomes zero, which refuses every creation. Erring toward refusal is the correct
    /// direction for an allowance — the permissive reading of a nonsense limit is the one that costs
    /// the operator money.
    /// </remarks>
    private static uint AllowanceOf(AccountSnapshot account)
    {
        return account.MaxCronEntries <= 0 ? 0u : (uint)account.MaxCronEntries;
    }

    /// <summary>The identifier a refused creation is recorded and logged against.</summary>
    /// <param name="command">The creation being refused.</param>
    /// <returns>The account's id.</returns>
    /// <remarks>
    /// The account rather than the entry, because a creation that has not reached the agent has no
    /// entry id to name — and the alternatives are worse. The command is forbidden outright
    /// (RULING 31, <see cref="CronAuditJournal"/>); an empty subject would leave a journal row that
    /// says an operation was refused without saying against what. The account id is exactly what a
    /// creation is attempted against, and it is an identifier rather than customer text.
    /// </remarks>
    private static string Subject(CreateCronEntryCommand command)
    {
        return command.AccountId.ToString();
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<CronEntryDto>> FailAsync(
        CreateCronEntryCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.CronEntryCreated, Subject(command), command.IpAddress, command.UserAgent, cancellationToken);

        return Result<CronEntryDto>.Fail(error);
    }
}
