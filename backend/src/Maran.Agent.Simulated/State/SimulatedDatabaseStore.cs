using Maran.Agent.Client.Services.DbService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.State;

/// <summary>
/// The databases of the simulated server, bounded and safe to share between requests. No password is
/// kept: the real server holds only a hash and every call that carries one returns nothing of it, so
/// the simulation has nothing to remember.
/// </summary>
public sealed class SimulatedDatabaseStore
{
    /// <summary>The size a database reports before anything is written into it.</summary>
    public const ulong EmptyDatabaseBytes = 16_384;

    /// <summary>Serialises every read and write of <see cref="_databases"/>.</summary>
    private readonly Lock _gate = new();

    /// <summary>The ceilings this store enforces.</summary>
    private readonly SimulatedAgentLimits _limits;

    /// <summary>The databases, keyed by owning account and database suffix.</summary>
    private readonly Dictionary<(string Account, string Database), SimulatedDatabase> _databases = [];

    /// <summary>The additional logins, keyed by owning account and login suffix. A dedicated login is not here: it lives on its database.</summary>
    private readonly HashSet<(string Account, string User)> _extraUsers = [];

    /// <summary>What each login holds on each database, keyed by account, login suffix and database suffix.</summary>
    private readonly Dictionary<(string Account, string User, string Database), IReadOnlyCollection<DatabasePrivilegeKind>>
        _grants = [];

    /// <summary>Creates an empty store bounded by <paramref name="limits"/>.</summary>
    /// <param name="limits">The ceilings this store enforces.</param>
    public SimulatedDatabaseStore(SimulatedAgentLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _limits = limits;
    }

    /// <summary>How many databases the server holds across every account.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _databases.Count;
            }
        }
    }

    /// <summary>Creates a database and its dedicated user, naming both under the account.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="databaseName">Database name suffix.</param>
    /// <param name="dbUsername">User name suffix.</param>
    /// <param name="sizeBytes">
    /// The size the database reports; seeds set it, a customer's creation passes <see cref="EmptyDatabaseBytes"/>.
    /// </param>
    /// <returns>
    /// The names as created, <c>AgentAlreadyExists</c> for a database the account already has, or
    /// <c>AgentLimitReached</c> when the server holds as many as it will.
    /// </returns>
    public Result<CreatedDatabaseDto> Create(string account, string databaseName, string dbUsername, ulong sizeBytes)
    {
        if (!SimulatedInput.IsBoundedName(account, _limits.MaxNameLength)
            || !SimulatedInput.IsBoundedName(databaseName, _limits.MaxNameLength)
            || !SimulatedInput.IsBoundedName(dbUsername, _limits.MaxNameLength))
        {
            return Result<CreatedDatabaseDto>.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            if (_databases.ContainsKey((account, databaseName)))
            {
                return Result<CreatedDatabaseDto>.Fail(SimulatedAgentErrors.AlreadyExists());
            }

            if (_databases.Count >= _limits.MaxDatabases)
            {
                return Result<CreatedDatabaseDto>.Fail(SimulatedAgentErrors.LimitReached());
            }

            var database = new SimulatedDatabase(account, databaseName, dbUsername, sizeBytes);
            _databases[(account, databaseName)] = database;

            return Result<CreatedDatabaseDto>.Ok(
                new CreatedDatabaseDto(database.FullDatabaseName, database.FullUserName));
        }
    }

    /// <summary>Drops a database and the user created beside it.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="databaseName">Database name suffix.</param>
    /// <param name="dbUsername">User name suffix; it must be the one the database was created with.</param>
    /// <returns>Success, or <c>AgentNotFound</c> when the account has no such database and user pair.</returns>
    public Result Drop(string account, string databaseName, string dbUsername)
    {
        lock (_gate)
        {
            if (!_databases.TryGetValue((account, databaseName), out var database) || database.UserSuffix != dbUsername)
            {
                return Result.Fail(SimulatedAgentErrors.NotFound());
            }

            _databases.Remove((account, databaseName));
            ForgetGrants(grant => { return grant.Account == account && grant.Database == databaseName; });
            ForgetGrants(grant => { return grant.Account == account && grant.User == dbUsername; });

            return Result.Ok();
        }
    }

    /// <summary>Checks that a database user exists, which is all a password change leaves to remember.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="dbUsername">User name suffix.</param>
    /// <returns>
    /// Success, or <c>AgentNotFound</c> when the account has no such user: the agent never creates one here.
    /// </returns>
    public Result RequireUser(string account, string dbUsername)
    {
        lock (_gate)
        {
            var exists = _extraUsers.Contains((account, dbUsername)) || _databases.Values.Any(
                database => { return database.Account == account && database.UserSuffix == dbUsername; });

            return exists ? Result.Ok() : Result.Fail(SimulatedAgentErrors.NotFound());
        }
    }

    /// <summary>Creates an additional login holding nothing, or refuses it as the real agent does.</summary>
    /// <param name="account">The owning account's system user name.</param>
    /// <param name="dbUsername">The login suffix.</param>
    /// <returns>The fully-qualified login name, or the agent's own typed failure.</returns>
    public Result<string> CreateUser(string account, string dbUsername)
    {
        if (!SimulatedInput.IsBoundedName(account, _limits.MaxNameLength)
            || !SimulatedInput.IsBoundedName(dbUsername, _limits.MaxNameLength))
        {
            return Result<string>.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            var dedicated = _databases.Values.Any(
                database => { return database.Account == account && database.UserSuffix == dbUsername; });
            if (dedicated || _extraUsers.Contains((account, dbUsername)))
            {
                return Result<string>.Fail(SimulatedAgentErrors.AlreadyExists());
            }

            if (_extraUsers.Count >= _limits.MaxDatabases)
            {
                return Result<string>.Fail(SimulatedAgentErrors.LimitReached());
            }

            _extraUsers.Add((account, dbUsername));

            return Result<string>.Ok($"{account}_{dbUsername}");
        }
    }

    /// <summary>Drops an additional login and what it held, or reports it is not there.</summary>
    /// <param name="account">The owning account's system user name.</param>
    /// <param name="dbUsername">The login suffix.</param>
    /// <returns>Success, or the agent's own not-found.</returns>
    public Result DropUser(string account, string dbUsername)
    {
        lock (_gate)
        {
            if (!_extraUsers.Remove((account, dbUsername)))
            {
                return Result.Fail(SimulatedAgentErrors.NotFound());
            }

            ForgetGrants(grant => { return grant.Account == account && grant.User == dbUsername; });

            return Result.Ok();
        }
    }

    /// <summary>Replaces what a login holds on one database, as the real agent converges it.</summary>
    /// <param name="account">The owning account's system user name.</param>
    /// <param name="dbUsername">The login suffix.</param>
    /// <param name="databaseName">The database suffix.</param>
    /// <param name="privileges">The complete set; empty revokes everything.</param>
    /// <returns>Success, or not-found for a login or database that is not there.</returns>
    public Result SetPrivileges(
        string account,
        string dbUsername,
        string databaseName,
        IReadOnlyCollection<DatabasePrivilegeKind> privileges)
    {
        lock (_gate)
        {
            var userExists = _extraUsers.Contains((account, dbUsername)) || _databases.Values.Any(
                database => { return database.Account == account && database.UserSuffix == dbUsername; });
            if (!userExists || !_databases.ContainsKey((account, databaseName)))
            {
                return Result.Fail(SimulatedAgentErrors.NotFound());
            }

            if (privileges.Count == 0)
            {
                _grants.Remove((account, dbUsername, databaseName));
            }
            else
            {
                _grants[(account, dbUsername, databaseName)] = privileges.Distinct().ToList();
            }

            return Result.Ok();
        }
    }

    /// <summary>Lists one account's databases, ordered by name so the answer is stable.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <returns>The account's databases; empty for an account that has none.</returns>
    public IReadOnlyList<SimulatedDatabase> ListFor(string account)
    {
        lock (_gate)
        {
            return _databases.Values
                .Where(database => { return database.Account == account; })
                .OrderBy(database => { return database.DatabaseSuffix; }, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>Reads the size of one database.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="databaseName">Database name suffix.</param>
    /// <returns>The size in bytes, or <c>AgentNotFound</c>.</returns>
    public Result<ulong> GetSize(string account, string databaseName)
    {
        lock (_gate)
        {
            return _databases.TryGetValue((account, databaseName), out var database)
                ? Result<ulong>.Ok(database.SizeBytes)
                : Result<ulong>.Fail(SimulatedAgentErrors.NotFound());
        }
    }

    /// <summary>Removes every recorded grant matching <paramref name="matches"/>. Called with the gate held.</summary>
    /// <param name="matches">Which grants to forget.</param>
    private void ForgetGrants(Func<(string Account, string User, string Database), bool> matches)
    {
        foreach (var key in _grants.Keys.Where(matches).ToList())
        {
            _grants.Remove(key);
        }
    }
}
