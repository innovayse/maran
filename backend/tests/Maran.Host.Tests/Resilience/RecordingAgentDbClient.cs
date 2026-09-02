using System.Net.Sockets;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DbService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner database client that records its arguments and can fail or hang on demand.</summary>
/// <remarks>
/// Hanging is as important as failing here. A retry proves the call passed through something; only a
/// call that never returns proves the something has a TIMEOUT, which is the whole reason the
/// decorator exists — a stuck unix socket must not hang the HTTP request that made the call.
/// </remarks>
internal sealed class RecordingAgentDbClient : IAgentDbClient
{
    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>When true, every call waits for its cancellation token instead of returning.</summary>
    public bool Hangs { get; set; }

    /// <summary>How many times any method on this client was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>The account username of the last call.</summary>
    public string? LastAccountUsername { get; private set; }

    /// <summary>The database name of the last call.</summary>
    public string? LastDatabaseName { get; private set; }

    /// <summary>The database username of the last call.</summary>
    public string? LastDbUsername { get; private set; }

    /// <summary>The password of the last call.</summary>
    public SensitiveString? LastPassword { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<CreatedDatabaseDto>> CreateAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDatabaseName = databaseName;
        LastDbUsername = dbUsername;
        LastPassword = password;

        await EnterAsync(cancellationToken);

        return Result<CreatedDatabaseDto>.Ok(new CreatedDatabaseDto("acc1_shop", "acc1_shopuser"));
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DropAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDatabaseName = databaseName;
        LastDbUsername = dbUsername;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDbUsername = dbUsername;
        LastPassword = password;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<DatabaseSummaryDto>>> ListAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;

        await EnterAsync(cancellationToken);

        return Result<IReadOnlyList<DatabaseSummaryDto>>.Ok([]);
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> GetSizeAsync(
        string accountUsername,
        string databaseName,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDatabaseName = databaseName;

        await EnterAsync(cancellationToken);

        return Result<ulong>.Ok(4096);
    }

    /// <summary>Counts the call and applies whichever misbehaviour the test asked for.</summary>
    /// <param name="cancellationToken">The token the pipeline's timeout cancels.</param>
    /// <returns>A task that completes once the call may return.</returns>
    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        if (Hangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        await Task.Yield();
    }
}
