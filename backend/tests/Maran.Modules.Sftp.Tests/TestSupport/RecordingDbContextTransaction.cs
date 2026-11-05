using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>
/// An <see cref="IDbContextTransaction"/> double that records how it ended, so a test can tell a
/// commit from a rollback on a provider that has no transactions of its own.
/// </summary>
/// <remarks>
/// The in-memory provider these tests run on ignores transactions: rows flushed by
/// <c>SaveChangesAsync</c> stay in the store whatever happens to the transaction. So what a test can
/// assert about a rollback is that the handler never committed and disposed the transaction, which
/// is what releases a real one as a rollback. <b>UNOBSERVED HERE: that the database really reverts
/// the rows</b>; that is PostgreSQL's behaviour and belongs to the integration suite.
/// </remarks>
public sealed class RecordingDbContextTransaction : IDbContextTransaction
{
    /// <inheritdoc/>
    public Guid TransactionId { get; } = Guid.NewGuid();

    /// <summary>Whether <see cref="CommitAsync"/> completed.</summary>
    public bool Committed { get; private set; }

    /// <summary>Whether the transaction was disposed, which for a real one is the rollback of anything uncommitted.</summary>
    public bool Disposed { get; private set; }

    /// <summary>Runs when a commit is attempted, so a test can observe the order of calls.</summary>
    public Action? OnCommit { get; set; }

    /// <summary>When set, the exception <see cref="CommitAsync"/> throws instead of committing.</summary>
    public Exception? CommitFailure { get; set; }

    /// <inheritdoc/>
    public void Commit()
    {
        throw new NotSupportedException("The handlers commit asynchronously.");
    }

    /// <inheritdoc/>
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        OnCommit?.Invoke();

        if (CommitFailure is not null)
        {
            throw CommitFailure;
        }

        Committed = true;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Rollback()
    {
        throw new NotSupportedException("The handlers roll back by disposing the transaction.");
    }

    /// <inheritdoc/>
    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("The handlers roll back by disposing the transaction.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Disposed = true;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Disposed = true;

        return ValueTask.CompletedTask;
    }
}
