using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DbService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A database agent double that holds every creation inside the agent call until a stated number of
/// callers have arrived there, so a test can put two panel requests in the same instant on purpose.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because a concurrency test that cannot LOSE is not a test.</b> The window the panel
/// has to close is the one between a request reading the account's database count and the row for that
/// database being committed, and the agent call sits inside it: two requests that pass the count check
/// and then meet here are exactly two requests that both believed they had room. Without the barrier
/// the two tasks would almost always be serialised by chance — the first would commit before the
/// second read — and the test would pass over code with no protection at all.
/// </para>
/// <para>
/// It counts creations and drops because the losing request must be observed to have UNDONE the
/// database it made: the count is how a test distinguishes "refused" from "refused and left an orphan
/// behind".
/// </para>
/// <para>
/// The calls this interface has that a creation never makes throw rather than answering. A double that
/// answered them would be a stub for a surface this test does not exercise, and a reader could not
/// tell which of its answers were load-bearing.
/// </para>
/// </remarks>
public sealed class BarrierDbAgent : IAgentDbClient
{
    /// <summary>Released once <see cref="_expected"/> callers are waiting inside the agent call.</summary>
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many callers must arrive before any of them is allowed to continue.</summary>
    private readonly int _expected;

    /// <summary>How many callers have arrived so far.</summary>
    private int _arrived;

    /// <summary>How many databases this double has been asked to create.</summary>
    private int _createCalls;

    /// <summary>How many databases this double has been asked to drop.</summary>
    private int _dropCalls;

    /// <summary>Creates the double.</summary>
    /// <param name="expectedCallers">
    /// How many creations must be in flight before any of them proceeds. One makes the double
    /// transparent, which is what the inverse control needs.
    /// </param>
    public BarrierDbAgent(int expectedCallers)
    {
        _expected = expectedCallers;
    }

    /// <summary>How many databases were created on the "server".</summary>
    public int CreateCalls
    {
        get
        {
            return Volatile.Read(ref _createCalls);
        }
    }

    /// <summary>How many databases were dropped again, which is what a compensating caller does.</summary>
    public int DropCalls
    {
        get
        {
            return Volatile.Read(ref _dropCalls);
        }
    }

    /// <summary>Creates the database, but not before every expected caller has reached this point.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="databaseName">The database name suffix.</param>
    /// <param name="dbUsername">The dedicated user's name suffix.</param>
    /// <param name="password">The generated password; ignored.</param>
    /// <param name="cancellationToken">Cancellation for the wait and the call.</param>
    /// <returns>The prefixed names, as the real agent reports them.</returns>
    public async Task<Result<CreatedDatabaseDto>> CreateAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createCalls);

        if (Interlocked.Increment(ref _arrived) >= _expected)
        {
            _released.TrySetResult();
        }

        await _released.Task.WaitAsync(cancellationToken);

        return Result<CreatedDatabaseDto>.Ok(new CreatedDatabaseDto(
            $"{accountUsername}_{databaseName}", $"{accountUsername}_{dbUsername}"));
    }

    /// <summary>Drops the database, recording that a caller compensated.</summary>
    /// <param name="accountUsername">The owning account's system user name; ignored.</param>
    /// <param name="databaseName">The database name suffix; ignored.</param>
    /// <param name="dbUsername">The dedicated user's suffix; ignored.</param>
    /// <param name="cancellationToken">Cancellation for the call; ignored.</param>
    /// <returns>Success.</returns>
    public Task<Result<bool>> DropAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _dropCalls);

        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <summary>Not exercised by a database creation.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="dbUsername">Ignored.</param>
    /// <param name="password">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers database creation and dropping only.");
    }

    /// <summary>Not exercised by a database creation.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<IReadOnlyList<DatabaseSummaryDto>>> ListAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers database creation and dropping only.");
    }

    /// <summary>Not exercised by a database creation.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="databaseName">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<ulong>> GetSizeAsync(
        string accountUsername,
        string databaseName,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers database creation and dropping only.");
    }
}
