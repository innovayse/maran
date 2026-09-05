using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sftp.Resources;
using Maran.Modules.Sftp.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;

using Microsoft.Extensions.Logging;
using Npgsql;

namespace Maran.Modules.Sftp.Commands.CreateSftpUser;

/// <summary>
/// Handles <see cref="CreateSftpUserCommand"/>: refuses what the plan or the account's own rows do
/// not allow, provisions the login through the agent, and only then records the row that owns it —
/// compensating on the host if that record cannot be written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: plan first, agent second, row third.</b> The plan limit is checked before the agent is
/// called at all, so a login the plan refuses never reaches the host. The agent runs before the row
/// because the two stores can disagree either way and this order decides which: a login with no row
/// is invisible and, as of the compensation below, short-lived, while a row with no login is a
/// customer told they have an SFTP account whose credentials are refused at the door.
/// </para>
/// <para>
/// <b>And the row failing is not the harmless half here, which is why the compensation exists.</b>
/// This module's password is shown once and stored nowhere. So a create that reached the host and
/// then failed to write its row leaves the customer with a live login, a live credential nobody
/// holds, and no row — and the obvious retry does NOT repair it, because the agent reports the
/// second creation as <c>AlreadyExists</c> and deliberately leaves the existing password alone. The
/// login would sit in <c>/etc/passwd</c> forever, counted against nothing, usable by nobody, and
/// still holding a key into the account's home.
/// </para>
/// <para>
/// <b>The chosen answer is COMPENSATION rather than reconciliation on the AlreadyExists path.</b>
/// A row-insert failure that leaves nothing owning the login deletes it again, so a retry starts
/// clean. The alternative — recognising the orphan on a later create and adopting it by resetting
/// its password — was rejected because it makes "this login already exists" sometimes mean "and I
/// have just changed its password", which is a create that silently re-credentials whatever it
/// finds under the name it was given.
/// </para>
/// <para>
/// <b>The duplicate branch is narrowed to SqlState 23505 and to nothing else</b>, because everything
/// below turns on it: a 23505 means a concurrent creation won and ITS row owns the login on the
/// host, so deleting would revoke a credential that customer has already been shown and cannot be
/// shown again; every other database failure — a dropped connection, a timeout, a constraint added
/// later — leaves nothing owning the login at all and falls through to the compensating branch.
/// Unlike the Databases module, there is no second narrowing by WHICH index fired: both unique
/// indexes here (<c>IX_SftpUsers_FullName</c> and <c>IX_SftpUsers_AccountId_Name</c>) say the same
/// thing — some row already claims this login — because a login is one name rather than a pair.
/// </para>
/// </remarks>
public sealed class CreateSftpUserCommandHandler
{
    /// <summary>
    /// The <c>useradd</c> name ceiling, which the prefixed login must fit inside.
    /// </summary>
    /// <remarks>
    /// Thirty-two bytes, which is what <c>useradd</c> accepts on both supported families and what
    /// the agent's own <c>SftpUserName</c> enforces. Answered here so the customer is told their
    /// name is too long rather than being handed the agent's refusal.
    /// </remarks>
    private const int SystemUserNameMaxLength = 32;

    /// <summary>
    /// How many characters the account prefix and its separator add to a suffix.
    /// </summary>
    /// <remarks>
    /// Counted, never assembled. Every agent call carries the SUFFIX and the account name, and the
    /// agent applies the prefix itself — so a request cannot express another tenant's login rather
    /// than merely being refused one. A full name built here and sent would give that property away
    /// for a length check.
    /// </remarks>
    private const int PrefixSeparatorLength = 1;

    /// <summary>Pre-compiled log delegate for a compensation that did not take.</summary>
    /// <remarks>
    /// Source-generated because what it reports is an orphaned login on the host that only an
    /// operator can clear, and a message an operator has to find must be searchable and structured
    /// rather than interpolated. It names the login and the agent's own error CODE, never the
    /// agent's text — and never the password, which by this point exists nowhere but in a local that
    /// is about to go out of scope.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogCompensationFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(CreateSftpUserCommandHandler)),
            "Created SFTP login {SftpUserName} could not be recorded and could not be deleted either "
            + "({AgentErrorCode}); it is now on the host with a password nobody holds and no row.");

    /// <summary>The Sftp module's database context, and this module's tenant boundary.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>The one window onto the owning account's system user name and plan allowance.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentSftpClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly SftpAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where a failed compensation is reported, since the customer is told nothing about it.</summary>
    private readonly ILogger<CreateSftpUserCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sftp module's database context.</param>
    /// <param name="accounts">The owning account's system user name and plan allowance.</param>
    /// <param name="agent">The agent client that provisions the login.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new row.</param>
    /// <param name="logger">Where a failed compensation is reported.</param>
    public CreateSftpUserCommandHandler(
        SftpDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSftpClient agent,
        SftpAuditJournal journal,
        IClock clock,
        ILogger<CreateSftpUserCommandHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Creates the login, refusing it before the host is touched when anything says no.</summary>
    /// <param name="command">The validated parameters; see <see cref="CreateSftpUserCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The new login and its password, shown once — or <c>AccountNotFound</c>,
    /// <c>SftpUserLimitReached</c>, <c>SftpUserNameTaken</c>, <c>SftpUserNameTooLong</c>,
    /// <c>SftpUserProvisioningFailed</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result<CreatedSftpUserDto>> HandleAsync(
        CreateSftpUserCommand command,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped: the directory answers null for an account this caller does not own, so a
        // guessed account id reads as "not found" rather than "forbidden".
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        // Spec §8: countable limits are enforced in the application at creation time, BEFORE the
        // agent is called — a login the plan refuses must never reach the host, or the panel has
        // made something it then has to remember to remove.
        //
        // KNOWN RACE, deliberately not solved here, exactly as the Sites and Databases modules
        // record: this is count-then-insert with no constraint behind it, so two concurrent
        // creations can both read N. Being one login over a plan limit is a billing discrepancy an
        // operator can see and correct, not a tenancy or availability failure.
        var existing = await _dbContext.SftpUsers
            .CountAsync(sftpUser => sftpUser.AccountId == command.AccountId, cancellationToken);
        if (existing >= account.MaxSftpUsers)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.SftpUserLimitReached), ErrorType.Conflict), cancellationToken);
        }

        // Whether the PREFIXED name fits is a question only this layer can answer, because it is the
        // only one that has both the suffix and the account's user name. Answered here rather than
        // left to the agent so the customer is told what is wrong with the name they typed.
        if (account.Username.Length + PrefixSeparatorLength + command.Name.Length > SystemUserNameMaxLength)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.SftpUserNameTooLong), ErrorType.Validation), cancellationToken);
        }

        // Asked of the account's OWN rows, through the tenant filter, and never of the host's
        // /etc/passwd. `deploy` is taken for this customer only if this customer already has one;
        // another tenant's `deploy` is a different system login entirely, because the prefix makes
        // it one. A check against the whole host would hand the first tenant to ask a name every
        // other tenant could then never use, which is the problem the prefix exists to solve.
        //
        // The AccountId predicate below is JOINTLY, not individually, observable — the same shape
        // the Databases module records. Removing it ALONE changes no test's outcome, because the
        // context's tenant query filter already scopes this read to the same account; removing it
        // TOGETHER with that filter is what breaks, and
        // A_name_another_tenant_uses_is_still_available_because_names_are_prefixed is the test that
        // dies. Stated here because a reader must not mistake it for a live check, and must not
        // delete it as dead code either.
        //
        // It is redundant only WHILE login names are prefixed per account. Prefixing is what makes a
        // cross-tenant collision on Name impossible by construction, which is precisely why the
        // filter has nothing left for this predicate to catch. The day that scheme changes, the
        // predicate is load-bearing again on its own — so it stays, stating the scope locally rather
        // than depending on an invariant it does not own.
        var nameTaken = await _dbContext.SftpUsers
            .AsNoTracking()
            .AnyAsync(
                sftpUser => sftpUser.AccountId == command.AccountId && sftpUser.Name == command.Name,
                cancellationToken);
        if (nameTaken)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.SftpUserNameTaken), ErrorType.Conflict), cancellationToken);
        }

        var password = ProvisionedPasswordGenerator.Generate();

        var provisioned = await _agent.CreateAsync(account.Username, command.Name, password, cancellationToken);
        if (!provisioned.IsSuccess)
        {
            return await FailAsync(command, provisioned.Error!, cancellationToken);
        }

        // The fully-qualified login is taken from the agent's answer, not rebuilt from the suffix and
        // the account name. Rebuilding would make this row's truth depend on the panel and the agent
        // agreeing about a separator forever, and the day they disagreed the panel would show the
        // customer a user name their SFTP client is refused with.
        var sftpUser = new SftpUser(
            Guid.NewGuid(), command.AccountId, command.Name, provisioned.Value, _clock.UtcNow);

        _dbContext.SftpUsers.Add(sftpUser);

        var recorded = await RecordAsync(sftpUser, account.Username, command, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return Result<CreatedSftpUserDto>.Fail(recorded.Error!);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.SftpUserCreated, sftpUser.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<CreatedSftpUserDto>.Ok(new CreatedSftpUserDto(
            sftpUser.Id,
            sftpUser.AccountId,
            sftpUser.Name,
            sftpUser.FullName,
            password,
            sftpUser.CreatedAt));
    }

    /// <summary>Writes the row for a login the agent has already made, compensating if it cannot.</summary>
    /// <param name="sftpUser">The row to write.</param>
    /// <param name="accountUsername">The owning account's system user name, which addresses the compensating delete.</param>
    /// <param name="command">The creation being recorded, for the journal's subject.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the typed failure the customer is answered with.</returns>
    private async Task<Result<bool>> RecordAsync(
        SftpUser sftpUser,
        string accountUsername,
        CreateSftpUserCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<bool>.Ok(true);
        }
        catch (DbUpdateException exception)
        {
            _dbContext.SftpUsers.Remove(sftpUser);

            // The ONE narrowing, and everything below turns on it. SqlState 23505 and nothing else
            // means a duplicate; every other database failure — a dropped connection, a timeout, a
            // constraint added later — falls through to the compensating branch. Catching
            // DbUpdateException wholesale and reporting every failure as "already taken" is the
            // message that discourages the retry which would repair the customer's login.
            if (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A concurrent creation won the race between the check above and this insert, and
                // ITS row owns the login now on the host. Nothing is orphaned, and deleting would
                // revoke a credential the winner has already been shown and cannot be shown again —
                // so this is the one post-create failure that must NOT compensate. The caller is
                // told the name is taken, which is true.
                return Result<bool>.Fail(await FailedErrorAsync(
                    command, Error.Of(nameof(ErrorMessages.SftpUserNameTaken), ErrorType.Conflict), cancellationToken));
            }

            // No winner, so there is nothing whose credential a delete could revoke — and there IS a
            // live login with a password nobody holds and no row, still holding a key into the
            // account's home. It goes.
            await CompensateAsync(accountUsername, command, cancellationToken);

            return Result<bool>.Fail(await FailedErrorAsync(
                    command, Error.Of(nameof(ErrorMessages.SftpUserProvisioningFailed), ErrorType.Failure), cancellationToken));
        }
    }

    /// <summary>Deletes a login the agent made but no row owns.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="command">The creation being undone; its suffix addresses the delete.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <remarks>
    /// Best effort, and logged rather than surfaced: the customer is already being told the creation
    /// failed, and a second failure here changes nothing they can act on. It is logged loudly
    /// because what remains is a login with a credential nobody holds and a key into the account's
    /// home, which only an operator can now find — and the log line is the only thing that will lead
    /// them to it.
    ///
    /// Addressed by SUFFIX, like every other agent call: the agent applies the account prefix
    /// itself, so a compensating delete cannot reach past this account even if the name it was
    /// handed were wrong.
    /// </remarks>
    private async Task CompensateAsync(
        string accountUsername,
        CreateSftpUserCommand command,
        CancellationToken cancellationToken)
    {
        var deleted = await _agent.DeleteAsync(accountUsername, command.Name, cancellationToken);
        if (!deleted.IsSuccess)
        {
            LogCompensationFailed(_logger, command.Name, deleted.Error!.Code, null);
        }
    }

    /// <summary>Journals a refused creation and hands back the code to answer with.</summary>
    /// <param name="command">The creation that was refused, whose name is the journal's subject.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns><paramref name="error"/>, unchanged.</returns>
    private async Task<Error> FailedErrorAsync(
        CreateSftpUserCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.SftpUserCreated, command.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return error;
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<CreatedSftpUserDto>> FailAsync(
        CreateSftpUserCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        return Result<CreatedSftpUserDto>.Fail(await FailedErrorAsync(command, error, cancellationToken));
    }
}
