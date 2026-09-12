using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FtpsService;
using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Domain.Policies;
using Maran.Modules.Ftp.Interfaces;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Ftp.Resources;
using Maran.Modules.Ftp.Services;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;

using Microsoft.Extensions.Logging;
using Npgsql;

namespace Maran.Modules.Ftp.Commands.CreateFtpUser;

/// <summary>
/// Handles <see cref="CreateFtpUserCommand"/>: refuses what the plan or the account's own rows do
/// not allow, provisions the login through the agent, and only then records the row that owns it —
/// compensating on the host if that record cannot be written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: plan first, agent second, row third.</b> The plan limit is checked before the agent is
/// called at all, so a login the plan refuses never reaches the host. The agent runs before the row
/// because the two stores can disagree either way and this order decides which: a login with no row
/// is invisible and, as of the compensation below, short-lived, while a row with no login is a
/// customer told they have an FTPS account whose credentials are refused at the door.
/// </para>
/// <para>
/// <b>Where the limit is actually enforced, and what it is enforced against.</b> The number comes
/// from <c>AccountSnapshot.MaxFtpUsers</c> — the plan's figure, read through <c>IAccountDirectory</c>
/// because this module may not read the Accounts schema — and it is compared against the count of
/// THIS module's rows for the account. Not against the host: the host's user namespace is shared
/// with the Sftp module and with whatever an operator created by hand, and counting logins the panel
/// did not create would make an operator's own <c>useradd</c> silently consume a customer's plan
/// allowance. The refusal happens here, in the backend, and nowhere else that matters: the SPA's own
/// arithmetic is advice and must err permissive (rules/security.md), so calling this endpoint
/// directly reaches exactly this check.
/// </para>
/// <para>
/// <b>The limit is checked twice, and the SECOND check is the one that holds.</b> The check above is
/// count-then-insert with nothing behind the count, so on its own two concurrent creations both read
/// N-1 and both succeed, leaving the account one login over its plan. It is kept because it refuses
/// the ordinary over-limit request before the host is touched at all. What closes the window is
/// <see cref="IFtpUserSlotGate"/>: the row is written under a per-account advisory lock, with the
/// count re-taken inside it, so the second of two simultaneous creations sees the first one's
/// committed row and is refused. The gate's own documentation says what each rejected alternative —
/// a unique index, an insert carrying the count, a serializable transaction, a counter column — is
/// blind to, and what the lock itself cannot see.
/// </para>
/// <para>
/// <b>The loser pays for it on the host, which is why the pre-check stays.</b> The gate runs after the
/// agent has made the login, so a request refused there leaves a login nothing owns and it is
/// compensated exactly as a failed write is. That is the trade this order buys: the frequent refusal
/// (a plan that is simply full) costs nothing, and the rare one (two requests inside the same
/// millisecond) costs a create and a delete on the host. The customer is told which of the two
/// happened, because "your plan is full" and "your plan filled up while this was being created" are
/// different sentences and only the second one is worth retrying after deleting something.
/// </para>
/// <para>
/// What the race never could produce, and still cannot, is two rows claiming one system login: that
/// is closed by the unique index on <c>FullName</c>, and the loser of THAT race arrives here as a
/// typed conflict rather than as a crash.
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
/// <b>The duplicate branch is narrowed to SqlState 23505 and to nothing else</b>, because everything
/// below turns on it: a 23505 means a concurrent creation won and ITS row owns the login on the
/// host, so deleting would revoke a credential that customer has already been shown and cannot be
/// shown again; every other database failure — a dropped connection, a timeout, a constraint added
/// later — leaves nothing owning the login at all and falls through to the compensating branch.
/// </para>
/// </remarks>
public sealed class CreateFtpUserCommandHandler
{
    /// <summary>
    /// The <c>useradd</c> name ceiling, which the prefixed login must fit inside.
    /// </summary>
    /// <remarks>
    /// Thirty-two bytes, which is what <c>useradd</c> accepts on both supported families and what
    /// the agent's own <c>FtpsUserName</c> enforces. Answered here so the customer is told their
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

    /// <summary>The agent client's stable code for "the host already holds this name".</summary>
    /// <remarks>
    /// The code string rather than <see cref="ErrorType.Conflict"/>, even though
    /// <c>AgentErrorTranslator</c> maps that one wire code and no other onto that kind today. The
    /// kind is a property of the whole translator and could gain another conflicting condition; the
    /// code names the one refusal this handler is entitled to re-read. The same shape the Backups
    /// and Firewall modules already use to recognise one agent code.
    /// </remarks>
    private const string AgentAlreadyExistsCode = "AgentAlreadyExists";

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
            new EventId(1, nameof(CreateFtpUserCommandHandler)),
            "Created FTPS login {FtpUserName} could not be recorded and could not be deleted either "
            + "({AgentErrorCode}); it is now on the host with a password nobody holds and no row.");

    /// <summary>The Ftp module's database context, and this module's tenant boundary.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>The one window onto the owning account's system user name and plan allowance.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentFtpsClient _agent;

    /// <summary>The atomic claim on the account's last free slot; see the race paragraph above.</summary>
    private readonly IFtpUserSlotGate _slotGate;

    /// <summary>This module's audit journal.</summary>
    private readonly FtpAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where a failed compensation is reported, since the customer is told nothing about it.</summary>
    private readonly ILogger<CreateFtpUserCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ftp module's database context.</param>
    /// <param name="accounts">The owning account's system user name and plan allowance.</param>
    /// <param name="agent">The agent client that provisions the login.</param>
    /// <param name="slotGate">Takes the account's last free slot atomically when the row is written.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new row.</param>
    /// <param name="logger">Where a failed compensation is reported.</param>
    public CreateFtpUserCommandHandler(
        FtpDbContext dbContext,
        IAccountDirectory accounts,
        IAgentFtpsClient agent,
        IFtpUserSlotGate slotGate,
        FtpAuditJournal journal,
        IClock clock,
        ILogger<CreateFtpUserCommandHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _slotGate = slotGate;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Creates the login, refusing it before the host is touched when anything says no.</summary>
    /// <param name="command">The validated parameters; see <see cref="CreateFtpUserCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The new login and its password, shown once — or <c>AccountNotFound</c>,
    /// <c>FtpUserLimitReached</c>, <c>FtpUserLimitReachedConcurrently</c>, <c>FtpUserNameTaken</c>, <c>FtpUserNameTooLong</c>,
    /// <c>FtpUserProvisioningFailed</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result<CreatedFtpUserDto>> HandleAsync(
        CreateFtpUserCommand command,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped: the directory answers null for an account this caller does not own, so a
        // guessed account id reads as "not found" rather than "forbidden".
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        // Spec §8: countable limits are enforced in the application at creation time, BEFORE the
        // agent is called — a login the plan refuses must never reach the host, or the panel has
        // made something it then has to remember to remove. The race this leaves open is described
        // on the type, together with what does and does not close it.
        var existing = await _dbContext.FtpUsers
            .CountAsync(ftpUser => ftpUser.AccountId == command.AccountId, cancellationToken);
        if (existing >= account.MaxFtpUsers)
        {
            return await FailAsync(
                command, Error.Of(nameof(ErrorMessages.FtpUserLimitReached), ErrorType.Conflict), cancellationToken);
        }

        // Whether the PREFIXED name fits is a question only this layer can answer, because it is the
        // only one that has both the suffix and the account's user name. Answered here rather than
        // left to the agent so the customer is told what is wrong with the name they typed.
        if (account.Username.Length + PrefixSeparatorLength + command.Name.Length > SystemUserNameMaxLength)
        {
            return await FailAsync(
                command, Error.Of(nameof(ErrorMessages.FtpUserNameTooLong), ErrorType.Validation), cancellationToken);
        }

        // Asked of the account's OWN rows, through the tenant filter, and never of the host's
        // /etc/passwd. `files` is taken for this customer only if this customer already has one;
        // another tenant's `files` is a different system login entirely, because the prefix makes it
        // one. It also deliberately does NOT ask the Sftp module whether it holds the name: that
        // would be a cross-module read of another module's schema, which the architecture tests
        // reject, and it would still be wrong — the authority on whether a system login exists is
        // the system, and it answers below, as AlreadyExists.
        //
        // The AccountId predicate is JOINTLY, not individually, observable: the context's tenant
        // query filter already scopes this read to the same account for a customer's request.
        // Removing it alone changes no test's outcome; removing it together with the filter is what
        // breaks. It stays because it states the scope locally rather than depending on an invariant
        // it does not own, and because for an ADMINISTRATOR — whom the filter does not narrow — it
        // is the only thing scoping the read at all.
        var nameTaken = await _dbContext.FtpUsers
            .AsNoTracking()
            .AnyAsync(
                ftpUser => ftpUser.AccountId == command.AccountId && ftpUser.Name == command.Name,
                cancellationToken);
        if (nameTaken)
        {
            return await FailAsync(
                command, Error.Of(nameof(ErrorMessages.FtpUserNameTaken), ErrorType.Conflict), cancellationToken);
        }

        var password = ProvisionedPasswordGenerator.Generate();

        var provisioned = await _agent.CreateUserAsync(
            new CreateFtpsUserArguments(account.Username, command.Name, password), cancellationToken);
        if (!provisioned.IsSuccess)
        {
            return await FailAsync(command, TranslateProvisioningFailure(provisioned.Error!), cancellationToken);
        }

        // The fully-qualified login is taken from the agent's answer, not rebuilt from the suffix and
        // the account name. Rebuilding would make this row's truth depend on the panel and the agent
        // agreeing about a separator forever, and the day they disagreed the panel would show the
        // customer a user name their FTPS client is refused with.
        var ftpUser = new FtpUser(
            Guid.NewGuid(), command.AccountId, command.Name, provisioned.Value, _clock.UtcNow);

        var recorded = await RecordAsync(
            ftpUser, account.Username, account.MaxFtpUsers, command, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return Result<CreatedFtpUserDto>.Fail(recorded.Error!);
        }

        await _journal.RecordSuccessAsync(
            FtpAuditJournal.FtpUserCreated, ftpUser.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<CreatedFtpUserDto>.Ok(new CreatedFtpUserDto(
            ftpUser.Id,
            ftpUser.AccountId,
            ftpUser.Name,
            ftpUser.FullName,
            FtpsProtocolName.Ftps,
            password,
            ftpUser.CreatedAt));
    }

    /// <summary>Re-reads the agent's refusal as a sentence about the NAME where that is what it means.</summary>
    /// <param name="error">The typed failure the agent client produced.</param>
    /// <returns>The name-taken failure for a host-side collision; otherwise <paramref name="error"/> unchanged.</returns>
    /// <remarks>
    /// <para>
    /// The login namespace is the host's and is shared with the Sftp module and with anything an
    /// operator created by hand, so the panel's own duplicate check above cannot see every
    /// collision — <c>useradd</c> refuses the name, the agent answers <c>AlreadyExists</c>, and that
    /// arrives here as the generic <see cref="AgentAlreadyExistsCode"/> sentence. Left untranslated the
    /// customer reads a message about the agent for a condition whose whole content is "pick another
    /// name", so the code becomes <c>FtpUserNameTaken</c> while the KIND stays
    /// <see cref="ErrorType.Conflict"/> — which is what the agent's refusal already meant.
    /// </para>
    /// <para>
    /// Only this one code is re-read. Every other agent failure keeps its own code and kind, because
    /// a name the customer could change is the only thing this translation can honestly claim.
    /// </para>
    /// </remarks>
    private static Error TranslateProvisioningFailure(Error error)
    {
        if (!string.Equals(error.Code, AgentAlreadyExistsCode, StringComparison.Ordinal))
        {
            return error;
        }

        return Error.Of(nameof(ErrorMessages.FtpUserNameTaken), ErrorType.Conflict);
    }

    /// <summary>Writes the row for a login the agent has already made, compensating if it cannot.</summary>
    /// <param name="ftpUser">The row to write.</param>
    /// <param name="accountUsername">The owning account's system user name, which addresses the compensating delete.</param>
    /// <param name="allowance">How many logins the account's plan allows, re-checked inside the gate.</param>
    /// <param name="command">The creation being recorded, for the journal's subject.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the typed failure the customer is answered with.</returns>
    private async Task<Result<bool>> RecordAsync(
        FtpUser ftpUser,
        string accountUsername,
        int allowance,
        CreateFtpUserCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await _slotGate.TryTakeAsync(ftpUser, allowance, cancellationToken))
            {
                return Result<bool>.Ok(true);
            }

            // The gate refused, which means another creation committed the account's last slot while
            // this one was on the host. Nothing owns the login this request made, so it goes — the
            // same compensation a failed write gets, for the same reason — and the refusal says the
            // plan filled up rather than that the name was taken, because the name is not the
            // problem and deleting a login the customer no longer needs is what unblocks them.
            await CompensateAsync(accountUsername, command, cancellationToken);

            return Result<bool>.Fail(await FailedErrorAsync(
                command,
                Error.Of(nameof(ErrorMessages.FtpUserLimitReachedConcurrently), ErrorType.Conflict),
                cancellationToken));
        }
        catch (DbUpdateException exception)
        {
            _dbContext.FtpUsers.Remove(ftpUser);

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
                // so this is the one post-create failure that must NOT compensate.
                return Result<bool>.Fail(await FailedErrorAsync(
                    command, Error.Of(nameof(ErrorMessages.FtpUserNameTaken), ErrorType.Conflict), cancellationToken));
            }

            // No winner, so there is nothing whose credential a delete could revoke — and there IS a
            // live login with a password nobody holds and no row, still holding a key into the
            // account's home. It goes.
            await CompensateAsync(accountUsername, command, cancellationToken);

            return Result<bool>.Fail(await FailedErrorAsync(
                command,
                Error.Of(nameof(ErrorMessages.FtpUserProvisioningFailed), ErrorType.Failure),
                cancellationToken));
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
    /// Addressed by SUFFIX and by the ACCOUNT, like every other agent call here: the agent applies
    /// the prefix itself and checks the candidate's passwd home against that account's jail, so a
    /// compensating delete cannot reach past this account even if the name it was handed were wrong.
    /// </remarks>
    private async Task CompensateAsync(
        string accountUsername,
        CreateFtpUserCommand command,
        CancellationToken cancellationToken)
    {
        var deleted = await _agent.DeleteUserAsync(accountUsername, command.Name, cancellationToken);
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
        CreateFtpUserCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            FtpAuditJournal.FtpUserCreated, command.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return error;
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<CreatedFtpUserDto>> FailAsync(
        CreateFtpUserCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        return Result<CreatedFtpUserDto>.Fail(await FailedErrorAsync(command, error, cancellationToken));
    }
}
