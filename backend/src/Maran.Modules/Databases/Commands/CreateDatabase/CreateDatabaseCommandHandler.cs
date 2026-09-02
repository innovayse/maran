using Maran.Agent.Client.Interfaces;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;

using Microsoft.Extensions.Logging;
using Npgsql;

namespace Maran.Modules.Databases.Commands.CreateDatabase;

/// <summary>
/// Handles <see cref="CreateDatabaseCommand"/>: refuses what the plan or the account's own rows do
/// not allow, provisions the database and its dedicated user through the agent, and only then
/// records the row that owns them — compensating on the server if that record cannot be written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: plan first, agent second, row third.</b> The plan limit is checked before the agent is
/// called at all, so a database the plan refuses never reaches the server. The agent runs before the
/// row because the two stores can disagree either way and this order decides which: a database with
/// no row is invisible and, as of the compensation below, short-lived, while a row with no database
/// is a customer told they have a database whose connection string does not work.
/// </para>
/// <para>
/// <b>And the row failing is not the harmless half here, which is why the compensation exists.</b>
/// This module's password is shown once and stored nowhere. So a create that reached the server and
/// then failed to write its row leaves the customer with a live database, a live credential nobody
/// holds, and no row — and the obvious retry does NOT repair it, because the agent reports the
/// second creation as <c>AlreadyExists</c> and deliberately leaves the existing password alone. The
/// database would sit there forever, counted against nothing, reachable by nobody.
/// </para>
/// <para>
/// <b>The chosen answer is COMPENSATION rather than reconciliation on the AlreadyExists path.</b>
/// A row-insert failure that leaves nothing owning the database drops it again, so a retry starts
/// clean. The alternative — recognising the orphan on a later create and adopting it by resetting
/// its password — was rejected because it makes "this database already exists" sometimes mean "and
/// I have just changed its password", which is a create that silently re-credentials whatever it
/// finds under the name it was given.
/// </para>
/// <para>
/// The duplicate branch is narrowed to SqlState 23505 and to nothing else, and then narrowed again
/// by WHICH constraint fired, because those two cases need opposite treatment: a conflict on the
/// database's own identity means a concurrent creation won and ITS row owns the database on the
/// server, so compensating would destroy the winner's data; a conflict on the dedicated user means
/// no row owns the database this request just made, so it is an orphan like any other.
/// </para>
/// </remarks>
public sealed class CreateDatabaseCommandHandler
{
    /// <summary>MySQL's identifier ceiling, which the prefixed database name must fit inside.</summary>
    private const int MySqlIdentifierMaxLength = 64;

    /// <summary>
    /// MySQL's user-name ceiling, which the prefixed user name must fit inside.
    /// </summary>
    /// <remarks>
    /// Shorter than the database limit, and it matters more: older servers TRUNCATE a longer user
    /// name rather than refusing it, and a truncated name is how two accounts silently end up
    /// sharing one MySQL login.
    /// </remarks>
    private const int MySqlUserNameMaxLength = 32;

    /// <summary>
    /// How many characters the account prefix and its separator add to a suffix.
    /// </summary>
    /// <remarks>
    /// Counted, never assembled. Every agent call carries the SUFFIX and the account name, and the
    /// agent applies the prefix itself — so a request cannot express another tenant's database
    /// rather than merely being refused one. A full name built here and sent would give that
    /// property away for a length check.
    /// </remarks>
    private const int PrefixSeparatorLength = 1;

    /// <summary>The unique indexes that mean "this exact database is already recorded".</summary>
    /// <remarks>
    /// Named rather than inferred, because the treatment of a 23505 depends entirely on which of
    /// them fired: these two mean somebody else's row already owns the database on the server, and
    /// dropping it would destroy their data.
    /// </remarks>
    private static readonly string[] DatabaseIdentityConstraints =
        ["IX_Databases_FullName", "IX_Databases_AccountId_Name"];

    /// <summary>Pre-compiled log delegate for a compensation that did not take.</summary>
    /// <remarks>
    /// Source-generated because what it reports is an orphan on the MySQL server that only an
    /// operator can clear, and a message an operator has to find must be searchable and structured
    /// rather than interpolated. It names the database and the agent's own error CODE, never the
    /// agent's text — and never the password, which by this point exists nowhere but in a local that
    /// is about to go out of scope.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogCompensationFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(CreateDatabaseCommandHandler)),
            "Created database {DatabaseName} could not be recorded and could not be dropped either "
            + "({AgentErrorCode}); it is now on the server with a password nobody holds and no row.");

    /// <summary>The Databases module's database context, and this module's tenant boundary.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>The one window onto the owning account's system user name and plan allowance.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the MySQL server.</summary>
    private readonly IAgentDbClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly DatabaseAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where a failed compensation is reported, since the customer is told nothing about it.</summary>
    private readonly ILogger<CreateDatabaseCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    /// <param name="accounts">The owning account's system user name and plan allowance.</param>
    /// <param name="agent">The agent client that provisions the database.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new row.</param>
    /// <param name="logger">Where a failed compensation is reported.</param>
    public CreateDatabaseCommandHandler(
        DatabasesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentDbClient agent,
        DatabaseAuditJournal journal,
        IClock clock,
        ILogger<CreateDatabaseCommandHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Creates the database, refusing it before the server is touched when anything says no.</summary>
    /// <param name="command">The validated parameters; see <see cref="CreateDatabaseCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The new database and its password, shown once — or <c>AccountNotFound</c>,
    /// <c>DatabaseLimitReached</c>, <c>DatabaseNameTaken</c>, <c>DatabaseUserNameTaken</c>,
    /// <c>DatabaseNameTooLong</c>, <c>DatabaseUserNameTooLong</c>,
    /// <c>DatabaseProvisioningFailed</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result<CreatedDatabaseDto>> HandleAsync(
        CreateDatabaseCommand command,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped: the directory answers null for an account this caller does not own, so a
        // guessed account id reads as "not found" rather than "forbidden".
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, nameof(ErrorMessages.AccountNotFound), cancellationToken);
        }

        // Spec §8: countable limits are enforced in the application at creation time, BEFORE the
        // agent is called — a database the plan refuses must never reach the server, or the panel
        // has made something it then has to remember to remove.
        //
        // KNOWN RACE, deliberately not solved here, exactly as the Sites module records: this is
        // count-then-insert with no constraint behind it, so two concurrent creations can both read
        // N. Being one database over a plan limit is a billing discrepancy an operator can see and
        // correct, not a tenancy or availability failure.
        var existing = await _dbContext.Databases
            .CountAsync(database => database.AccountId == command.AccountId, cancellationToken);
        if (existing >= account.MaxDatabases)
        {
            return await FailAsync(command, nameof(ErrorMessages.DatabaseLimitReached), cancellationToken);
        }

        // Whether the PREFIXED names fit is a question only this layer can answer, because it is the
        // only one that has both the suffix and the account's user name. Answered here rather than
        // left to the agent so the customer is told which of their two names is too long.
        if (account.Username.Length + PrefixSeparatorLength + command.Name.Length > MySqlIdentifierMaxLength)
        {
            return await FailAsync(command, nameof(ErrorMessages.DatabaseNameTooLong), cancellationToken);
        }

        if (account.Username.Length + PrefixSeparatorLength + command.DbUserName.Length > MySqlUserNameMaxLength)
        {
            return await FailAsync(command, nameof(ErrorMessages.DatabaseUserNameTooLong), cancellationToken);
        }

        // Asked of the account's OWN rows, through the tenant filter, and never of the server's
        // names. `shop` is taken for this customer only if this customer already has one; another
        // tenant's `shop` is a different MySQL database entirely, because the prefix makes it one.
        // A check against the whole server would hand the first tenant to ask a name every other
        // tenant could then never use, which is the problem the prefix exists to solve.
        //
        // The AccountId predicate below is JOINTLY, not individually, observable. Removing it ALONE
        // changes no test's outcome, because the context's tenant query filter already scopes this
        // read to the same account; removing it TOGETHER with that filter is what breaks, and
        // A_name_another_tenant_already_uses_is_still_available_because_names_are_prefixed is the
        // test that dies. Stated here because a reader must not mistake it for a live check, and
        // must not delete it as dead code either.
        //
        // It is redundant only WHILE database names are prefixed per account. Prefixing is what
        // makes a cross-tenant collision on Name impossible by construction, which is precisely why
        // the filter has nothing left for this predicate to catch. The day that scheme changes, the
        // predicate is load-bearing again on its own — so it stays, stating the scope locally rather
        // than depending on an invariant it does not own.
        var nameTaken = await _dbContext.Databases
            .AsNoTracking()
            .AnyAsync(
                database => database.AccountId == command.AccountId && database.Name == command.Name,
                cancellationToken);
        if (nameTaken)
        {
            return await FailAsync(command, nameof(ErrorMessages.DatabaseNameTaken), cancellationToken);
        }

        // A MySQL user is one login. Two of the account's databases sharing one would mean a reset
        // for either silently re-credentials both, and a drop of either takes the other's login with
        // it, so the pairing is one-to-one and this is where a customer is told so.
        var userNameTaken = await _dbContext.Databases
            .AsNoTracking()
            .AnyAsync(
                database => database.AccountId == command.AccountId
                    && database.DbUserNameSuffix == command.DbUserName,
                cancellationToken);
        if (userNameTaken)
        {
            return await FailAsync(command, nameof(ErrorMessages.DatabaseUserNameTaken), cancellationToken);
        }

        var password = ProvisionedPasswordGenerator.Generate();

        var provisioned = await _agent.CreateAsync(
            account.Username,
            command.Name,
            command.DbUserName,
            password,
            cancellationToken);
        if (!provisioned.IsSuccess)
        {
            return await FailAsync(command, provisioned.Error!.Code, cancellationToken);
        }

        // The fully-qualified names are taken from the agent's answer, not rebuilt from the suffix
        // and the account name. Rebuilding would make this row's truth depend on the panel and the
        // agent agreeing about a separator forever, and the day they disagreed a drop would address
        // the wrong database or none.
        var database = new Database(
            Guid.NewGuid(),
            command.AccountId,
            command.Name,
            provisioned.Value.DatabaseName,
            provisioned.Value.DbUsername,
            command.DbUserName,
            _clock.UtcNow);

        _dbContext.Databases.Add(database);

        var recorded = await RecordAsync(database, account.Username, command, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return Result<CreatedDatabaseDto>.Fail(recorded.Error!);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.DatabaseCreated, database.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<CreatedDatabaseDto>.Ok(new CreatedDatabaseDto(
            database.Id,
            database.AccountId,
            database.Name,
            database.FullName,
            database.DbUserName,
            password,
            database.CreatedAt));
    }

    /// <summary>Whether a violated constraint means "this exact database is already recorded".</summary>
    /// <param name="constraintName">The constraint PostgreSQL named in its refusal, when it named one.</param>
    /// <returns>
    /// True when the conflict is on the database's own identity, in which case a concurrent
    /// creation's row owns the database on the server and it must NOT be dropped.
    /// </returns>
    /// <remarks>
    /// An unnamed constraint answers TRUE, which is the conservative direction rather than the
    /// convenient one. A wrong "yes" leaves an orphan an operator can clear; a wrong "no" drops a
    /// database whose data belongs to whoever won the race. When PostgreSQL does not tell us which
    /// key it was, guessing must not be able to destroy anything.
    /// </remarks>
    private static bool ClaimsTheSameDatabase(string? constraintName)
    {
        return constraintName is null
            || DatabaseIdentityConstraints.Contains(constraintName, StringComparer.Ordinal);
    }

    /// <summary>Writes the row for a database the agent has already made, compensating if it cannot.</summary>
    /// <param name="database">The row to write.</param>
    /// <param name="accountUsername">The owning account's system user name, which addresses the compensating drop.</param>
    /// <param name="command">The creation being recorded, for the journal's subject.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the typed failure the customer is answered with.</returns>
    private async Task<Result<bool>> RecordAsync(
        Database database,
        string accountUsername,
        CreateDatabaseCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<bool>.Ok(true);
        }
        catch (DbUpdateException exception)
        {
            _dbContext.Databases.Remove(database);

            // The ONE narrowing, and everything below turns on it. SqlState 23505 and nothing else
            // means a duplicate; every other database failure — a dropped connection, a timeout, a
            // constraint added later — falls through to the compensating branch. The previous plan's
            // Ssl module caught DbUpdateException wholesale and reported every failure as "already
            // taken", which is the message that discourages the retry that would repair it.
            if (exception.InnerException is not PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                } duplicate)
            {
                // No winner, so there is nothing whose data a drop could destroy — and there IS a
                // live database with a password nobody holds and no row. It goes.
                await CompensateAsync(accountUsername, command, cancellationToken);

                return Result<bool>.Fail(Error.Of(await FailedCodeAsync(
                    command, nameof(ErrorMessages.DatabaseProvisioningFailed), cancellationToken)));
            }

            if (ClaimsTheSameDatabase(duplicate.ConstraintName))
            {
                // A concurrent creation won the race between the check above and this insert, and
                // ITS row owns the database now on the server. Nothing is orphaned, and dropping
                // would destroy the winner's data — so this is the one post-create failure that
                // must NOT compensate. The caller is told the name is taken, which is true.
                return Result<bool>.Fail(Error.Of(
                    await FailedCodeAsync(command, nameof(ErrorMessages.DatabaseNameTaken), cancellationToken)));
            }

            // The conflict is on the dedicated USER, which another of this account's databases
            // already claims. Nothing owns the database this request just created, so it is exactly
            // the orphan described above and is compensated for like any other.
            await CompensateAsync(accountUsername, command, cancellationToken);

            return Result<bool>.Fail(Error.Of(
                await FailedCodeAsync(command, nameof(ErrorMessages.DatabaseUserNameTaken), cancellationToken)));
        }
    }

    /// <summary>Drops a database the agent made but no row owns.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="command">The creation being undone; its suffixes address the drop.</param>
    /// <param name="cancellationToken">Cancels the drop.</param>
    /// <remarks>
    /// Best effort, and logged rather than surfaced: the customer is already being told the creation
    /// failed, and a second failure here changes nothing they can act on. It is logged loudly
    /// because what remains is a database with a credential nobody holds, which only an operator can
    /// now find — and the log line is the only thing that will lead them to it.
    ///
    /// Addressed by SUFFIX, like every other agent call: the agent applies the account prefix
    /// itself, so a compensating drop cannot reach past this account even if the names it was handed
    /// were wrong.
    /// </remarks>
    private async Task CompensateAsync(
        string accountUsername,
        CreateDatabaseCommand command,
        CancellationToken cancellationToken)
    {
        var dropped = await _agent.DropAsync(
            accountUsername, command.Name, command.DbUserName, cancellationToken);
        if (!dropped.IsSuccess)
        {
            LogCompensationFailed(_logger, command.Name, dropped.Error!.Code, null);
        }
    }

    /// <summary>Journals a refused creation and hands back the code to answer with.</summary>
    /// <param name="command">The creation that was refused, whose name is the journal's subject.</param>
    /// <param name="code">The machine-stable code to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns><paramref name="code"/>, unchanged.</returns>
    private async Task<string> FailedCodeAsync(
        CreateDatabaseCommand command,
        string code,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.DatabaseCreated, command.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return code;
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="code">The machine-stable code to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="code"/>.</returns>
    private async Task<Result<CreatedDatabaseDto>> FailAsync(
        CreateDatabaseCommand command,
        string code,
        CancellationToken cancellationToken)
    {
        return Result<CreatedDatabaseDto>.Fail(
            Error.Of(await FailedCodeAsync(command, code, cancellationToken)));
    }
}
