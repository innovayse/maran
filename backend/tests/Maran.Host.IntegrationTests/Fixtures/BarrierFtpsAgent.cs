using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FtpsService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An FTPS agent double that holds every creation inside the agent call until a stated number of
/// callers have arrived there, so a test can put two panel requests in the same instant on purpose.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because a concurrency test that cannot LOSE is not a test.</b> The window the panel
/// has to close is the one between a request reading the account's login count and the row for that
/// login being committed, and the agent call sits inside it: two requests that pass the count check
/// and then meet here are exactly two requests that both believed they had room. Without the barrier
/// the two tasks would almost always be serialised by chance — the first would commit before the
/// second read — and the test would pass over code with no protection at all.
/// </para>
/// <para>
/// It counts creations and deletions because the losing request must be observed to have UNDONE the
/// login it made on the host: the count is how a test distinguishes "refused" from "refused and left
/// an orphan behind".
/// </para>
/// <para>
/// The calls this interface has that a login creation never makes throw rather than answering. A
/// double that answered them would be a stub for a surface this test does not exercise, and a reader
/// could not tell which of its answers were load-bearing.
/// </para>
/// </remarks>
public sealed class BarrierFtpsAgent : IAgentFtpsClient
{
    /// <summary>Released once <see cref="_expected"/> callers are waiting inside the agent call.</summary>
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many callers must arrive before any of them is allowed to continue.</summary>
    private readonly int _expected;

    /// <summary>How many callers have arrived so far.</summary>
    private int _arrived;

    /// <summary>How many logins this double has been asked to create.</summary>
    private int _createCalls;

    /// <summary>How many logins this double has been asked to delete.</summary>
    private int _deleteCalls;

    /// <summary>Creates the double.</summary>
    /// <param name="expectedCallers">
    /// How many creations must be in flight before any of them proceeds. One makes the double
    /// transparent, which is what the inverse control needs.
    /// </param>
    public BarrierFtpsAgent(int expectedCallers)
    {
        _expected = expectedCallers;
    }

    /// <summary>How many logins were created on the "host".</summary>
    public int CreateUserCalls
    {
        get
        {
            return Volatile.Read(ref _createCalls);
        }
    }

    /// <summary>How many logins were deleted again, which is what a compensating caller does.</summary>
    public int DeleteUserCalls
    {
        get
        {
            return Volatile.Read(ref _deleteCalls);
        }
    }

    /// <summary>Creates the login, but not before every expected caller has reached this point.</summary>
    /// <param name="arguments">The account, the suffix and the password; only the names are used.</param>
    /// <param name="cancellationToken">Cancellation for the wait and the call.</param>
    /// <returns>The prefixed login name, as the real agent reports it.</returns>
    public async Task<Result<string>> CreateUserAsync(
        CreateFtpsUserArguments arguments,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createCalls);

        if (Interlocked.Increment(ref _arrived) >= _expected)
        {
            _released.TrySetResult();
        }

        await _released.Task.WaitAsync(cancellationToken);

        return Result<string>.Ok($"{arguments.AccountUsername}_{arguments.FtpsUsername}");
    }

    /// <summary>Deletes the login, recording that a caller compensated.</summary>
    /// <param name="accountUsername">The owning account's system user name; ignored.</param>
    /// <param name="ftpsUsername">The login suffix; ignored.</param>
    /// <param name="cancellationToken">Cancellation for the call; ignored.</param>
    /// <returns>Success.</returns>
    public Task<Result<bool>> DeleteUserAsync(
        string accountUsername,
        string ftpsUsername,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _deleteCalls);

        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <summary>Not exercised by a login creation.</summary>
    /// <param name="hostname">Ignored.</param>
    /// <param name="passivePortMin">Ignored.</param>
    /// <param name="passivePortMax">Ignored.</param>
    /// <param name="passiveAddress">Ignored.</param>
    /// <param name="maxClients">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<FtpsStatusDto>> EnableAsync(
        string hostname,
        uint passivePortMin,
        uint passivePortMax,
        string passiveAddress,
        uint maxClients,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers login creation and deletion only.");
    }

    /// <summary>Not exercised by a login creation.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<FtpsStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers login creation and deletion only.");
    }

    /// <summary>Not exercised by a login creation.</summary>
    /// <param name="hostname">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<FtpsStatusDto>> GetStatusAsync(string hostname, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers login creation and deletion only.");
    }

    /// <summary>Not exercised by a login creation.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<FtpsStatusDto>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers login creation and deletion only.");
    }

    /// <summary>Not exercised by a login creation.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="ftpsUsername">Ignored.</param>
    /// <param name="password">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string ftpsUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers login creation and deletion only.");
    }
}
