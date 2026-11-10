using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// A transaction that tells its lock when it is released: by commit, rollback or disposal, whichever comes
/// first. It wraps the in-memory provider's no-op transaction, so what it adds is only the observation.
/// </summary>
public sealed class CountedTransaction : IDbContextTransaction
{
    /// <summary>The provider's own transaction.</summary>
    private readonly IDbContextTransaction _inner;

    /// <summary>Runs once, when the lock this transaction stands for is released.</summary>
    private readonly Action _released;

    /// <summary>Whether the release was already reported.</summary>
    private bool _isReleased;

    /// <summary>Wraps a transaction.</summary>
    /// <param name="inner">The provider's transaction.</param>
    /// <param name="released">Runs once, when the lock is released.</param>
    public CountedTransaction(IDbContextTransaction inner, Action released)
    {
        _inner = inner;
        _released = released;
    }

    /// <inheritdoc />
    public Guid TransactionId
    {
        get
        {
            return _inner.TransactionId;
        }
    }

    /// <inheritdoc />
    public void Commit()
    {
        _inner.Commit();
        Release();
    }

    /// <inheritdoc />
    public void Rollback()
    {
        _inner.Rollback();
        Release();
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _inner.CommitAsync(cancellationToken);
        Release();
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _inner.RollbackAsync(cancellationToken);
        Release();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _inner.Dispose();
        Release();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        Release();
    }

    /// <summary>Reports the release, once.</summary>
    private void Release()
    {
        if (_isReleased)
        {
            return;
        }

        _isReleased = true;
        _released();
    }
}
