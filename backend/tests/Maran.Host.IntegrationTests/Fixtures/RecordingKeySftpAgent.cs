using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SftpService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An SFTP agent double for the key endpoints: it keeps the key set the panel last sent for each login
/// exactly as a real host's file would hold it, records every end-sessions request, and can be told to
/// refuse either call.
/// </summary>
/// <remarks>
/// <para>
/// It models the property the panel relies on: the agent converges to the COMPLETE set it is sent, so
/// <see cref="KeysOf"/> is what a host would honour and a test compares it with what the panel lists.
/// A refused set leaves the previous one in place, as the real agent's atomic write does.
/// </para>
/// <para>
/// The calls a key change never makes throw rather than answering, so a handler that started calling
/// them would fail loudly instead of being quietly satisfied by a stub.
/// </para>
/// </remarks>
public sealed class RecordingKeySftpAgent : IAgentSftpClient
{
    /// <summary>The key set each login would hold on the host, keyed by <c>account/login</c>.</summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _sets = [];

    /// <summary>Every set the panel sent, in arrival order.</summary>
    private readonly List<SentKeySet> _setCalls = [];

    /// <summary>Every end-sessions request, in arrival order, as <c>account/login</c>.</summary>
    private readonly List<string> _endCalls = [];

    /// <summary>Guards the collections: a request and the test's assertions run on different threads.</summary>
    private readonly object _gate = new();

    /// <summary>The failure the next set requests answer with, or null for success.</summary>
    private Error? _setFailure;

    /// <summary>The failure the end-sessions requests answer with, or null for success.</summary>
    private Error? _endFailure;

    /// <summary>Every set the panel sent, in arrival order.</summary>
    public IReadOnlyList<SentKeySet> SetCalls
    {
        get
        {
            lock (_gate)
            {
                return [.. _setCalls];
            }
        }
    }

    /// <summary>Every end-sessions request, as <c>account/login</c>, in arrival order.</summary>
    public IReadOnlyList<string> EndCalls
    {
        get
        {
            lock (_gate)
            {
                return [.. _endCalls];
            }
        }
    }

    /// <summary>Makes every set request answer with <paramref name="error"/>, keeping the host's set.</summary>
    /// <param name="error">The failure, or null to answer success again.</param>
    public void RefuseSets(Error? error)
    {
        lock (_gate)
        {
            _setFailure = error;
        }
    }

    /// <summary>Makes every end-sessions request answer with <paramref name="error"/>.</summary>
    /// <param name="error">The failure, or null to answer success again.</param>
    public void RefuseEndSessions(Error? error)
    {
        lock (_gate)
        {
            _endFailure = error;
        }
    }

    /// <summary>The key set a login holds on the "host" now.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="sftpUsername">The login's name suffix.</param>
    /// <returns>The last accepted set, or empty when none was ever accepted.</returns>
    public IReadOnlyList<string> KeysOf(string accountUsername, string sftpUsername)
    {
        lock (_gate)
        {
            return _sets.TryGetValue(Name(accountUsername, sftpUsername), out var keys) ? keys : [];
        }
    }

    /// <summary>Records the set, and keeps it unless a refusal was scripted.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="sftpUsername">The login's name suffix.</param>
    /// <param name="publicKeys">The complete set.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Success, or the scripted failure.</returns>
    public Task<Result> SetAuthorizedKeysAsync(
        string accountUsername,
        string sftpUsername,
        IReadOnlyList<string> publicKeys,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _setCalls.Add(new SentKeySet(accountUsername, sftpUsername, [.. publicKeys]));
            if (_setFailure is not null)
            {
                return Task.FromResult(Result.Fail(_setFailure));
            }

            _sets[Name(accountUsername, sftpUsername)] = [.. publicKeys];
        }

        return Task.FromResult(Result.Ok());
    }

    /// <summary>Records the request, and answers the scripted failure or zero sessions ended.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="sftpUsername">The login's name suffix.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Zero ended sessions, or the scripted failure.</returns>
    public Task<Result<int>> EndSessionsAsync(
        string accountUsername,
        string sftpUsername,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _endCalls.Add(Name(accountUsername, sftpUsername));

            return Task.FromResult(_endFailure is null ? Result<int>.Ok(0) : Result<int>.Fail(_endFailure));
        }
    }

    /// <summary>Not exercised by a key change.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="sftpUsername">Ignored.</param>
    /// <param name="password">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<string>> CreateAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers key changes only.");
    }

    /// <summary>Not exercised by a key change.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="sftpUsername">Ignored.</param>
    /// <param name="password">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result> SetPasswordAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers key changes only.");
    }

    /// <summary>Not exercised by a key change.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="sftpUsername">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result> DeleteAsync(
        string accountUsername,
        string sftpUsername,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers key changes only.");
    }

    /// <summary>Not exercised by a key change.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="locked">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<AccountLoginLockOutcomeDto>> SetAccountLoginsLockedAsync(
        string accountUsername,
        bool locked,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers key changes only.");
    }

    /// <summary>Names a login the way the host does.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="sftpUsername">The login's name suffix.</param>
    private static string Name(string accountUsername, string sftpUsername)
    {
        return $"{accountUsername}/{sftpUsername}";
    }
}
