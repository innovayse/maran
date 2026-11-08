using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.State;

/// <summary>
/// The SFTP logins of the simulated server, bounded and safe to share between requests. No password
/// is kept, for the reason the real server keeps only a hash of it: nothing reads it back.
/// </summary>
public sealed class SimulatedSftpLoginStore
{
    /// <summary>How many host processes one session accounts for: sshd and its sftp child.</summary>
    public const int ProcessesPerSession = 2;

    /// <summary>Serialises every read and write of <see cref="_logins"/>.</summary>
    private readonly Lock _gate = new();

    /// <summary>The ceilings this store enforces.</summary>
    private readonly SimulatedAgentLimits _limits;

    /// <summary>The logins, keyed by owning account and name suffix.</summary>
    private readonly Dictionary<(string Account, string Suffix), SimulatedSftpLogin> _logins = [];

    /// <summary>Creates an empty store bounded by <paramref name="limits"/>.</summary>
    /// <param name="limits">The ceilings this store enforces.</param>
    public SimulatedSftpLoginStore(SimulatedAgentLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _limits = limits;
    }

    /// <summary>Creates a login with no keys; seeds pass the sessions it should already have open.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="suffix">Login name suffix.</param>
    /// <param name="openSessions">Sessions the login has open at creation; a customer's creation passes zero.</param>
    /// <returns>
    /// The full login name, <c>AgentAlreadyExists</c> for a login that is already there, or
    /// <c>AgentLimitReached</c> when the server holds as many as it will.
    /// </returns>
    public Result<string> Create(string account, string suffix, int openSessions)
    {
        if (!SimulatedInput.IsBoundedName(account, _limits.MaxNameLength)
            || !SimulatedInput.IsBoundedName(suffix, _limits.MaxNameLength))
        {
            return Result<string>.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            if (_logins.ContainsKey((account, suffix)))
            {
                return Result<string>.Fail(SimulatedAgentErrors.AlreadyExists());
            }

            if (_logins.Count >= _limits.MaxSftpLogins)
            {
                return Result<string>.Fail(SimulatedAgentErrors.LimitReached());
            }

            var login = new SimulatedSftpLogin(account, suffix, [], openSessions);
            _logins[(account, suffix)] = login;

            return Result<string>.Ok(login.FullName);
        }
    }

    /// <summary>Checks that a login exists, which is all a password change leaves to remember.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="suffix">Login name suffix.</param>
    /// <returns>Success, or <c>AgentNotFound</c>.</returns>
    public Result Require(string account, string suffix)
    {
        lock (_gate)
        {
            return _logins.ContainsKey((account, suffix)) ? Result.Ok() : Result.Fail(SimulatedAgentErrors.NotFound());
        }
    }

    /// <summary>Removes a login and nothing else.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="suffix">Login name suffix.</param>
    /// <returns>Success, or <c>AgentNotFound</c>.</returns>
    public Result Delete(string account, string suffix)
    {
        lock (_gate)
        {
            return _logins.Remove((account, suffix)) ? Result.Ok() : Result.Fail(SimulatedAgentErrors.NotFound());
        }
    }

    /// <summary>Ends every session one login has open.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="suffix">Login name suffix.</param>
    /// <returns>
    /// The host processes signalled — <see cref="ProcessesPerSession"/> for each session, zero for a
    /// login with none open — or <c>AgentNotFound</c> when the account holds no such login.
    /// </returns>
    public Result<int> EndSessions(string account, string suffix)
    {
        lock (_gate)
        {
            if (!_logins.TryGetValue((account, suffix), out var login))
            {
                return Result<int>.Fail(SimulatedAgentErrors.NotFound());
            }

            _logins[(account, suffix)] = login with { OpenSessions = 0 };

            return Result<int>.Ok(login.OpenSessions * ProcessesPerSession);
        }
    }

    /// <summary>Ends every session every login of one account has open.</summary>
    /// <param name="account">System username of the account.</param>
    /// <returns>How many sessions were ended; zero for an account with no login or none open.</returns>
    public uint EndAllSessions(string account)
    {
        lock (_gate)
        {
            uint ended = 0;
            foreach (var login in _logins.Values.Where(login => { return login.Account == account; }).ToList())
            {
                ended += (uint)login.OpenSessions;
                _logins[(login.Account, login.Suffix)] = login with { OpenSessions = 0 };
            }

            return ended;
        }
    }

    /// <summary>Replaces the whole set of public keys a login may authenticate with.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="suffix">Login name suffix.</param>
    /// <param name="publicKeys">The complete key set; empty clears every key.</param>
    /// <returns>
    /// Success, <c>AgentNotFound</c> when the account holds no such login, or <c>AgentInvalidInput</c>
    /// when a key is blank or too long or there are too many, in which case nothing is written.
    /// </returns>
    public Result ReplaceKeys(string account, string suffix, IReadOnlyList<string> publicKeys)
    {
        if (publicKeys.Count > _limits.MaxPublicKeysPerLogin
            || publicKeys.Any(key =>
            {
                return string.IsNullOrWhiteSpace(key)
                    || key.Length > _limits.MaxPublicKeyLength;
            }))
        {
            return Result.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            if (!_logins.TryGetValue((account, suffix), out var login))
            {
                return Result.Fail(SimulatedAgentErrors.NotFound());
            }

            _logins[(account, suffix)] = login with { PublicKeys = publicKeys.ToList() };

            return Result.Ok();
        }
    }

    /// <summary>Reads one login.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="suffix">Login name suffix.</param>
    /// <returns>The login, or <c>null</c> when the account holds none by that name.</returns>
    public SimulatedSftpLogin? Find(string account, string suffix)
    {
        lock (_gate)
        {
            return _logins.GetValueOrDefault((account, suffix));
        }
    }
}
