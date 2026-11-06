using Maran.Modules.Identity.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// The API key creation lock as this project can run it: a transaction that records whether it was
/// begun, committed or abandoned, without the PostgreSQL advisory lock the real one is built on.
/// </summary>
/// <remarks>
/// <b>UNOBSERVED HERE: that two simultaneous creations cannot both pass the key limit.</b> That is a
/// property of PostgreSQL, invisible to any test on this provider; what this stand-in shows is that
/// the handler takes the lock before it counts and commits it only for a key it stored.
/// </remarks>
public sealed class RecordingApiKeyCreationLock : IApiKeyCreationLock
{
    /// <summary>How many times the lock was taken.</summary>
    public int Begins { get; private set; }

    /// <summary>How many of those transactions were committed.</summary>
    public int Commits { get; private set; }

    /// <inheritdoc />
    public Task<IDbContextTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        Begins++;

        return Task.FromResult<IDbContextTransaction>(new RecordingTransaction(this));
    }

    /// <summary>A transaction that only counts its commit.</summary>
    private sealed class RecordingTransaction : IDbContextTransaction
    {
        /// <summary>The lock whose counters this transaction updates.</summary>
        private readonly RecordingApiKeyCreationLock _owner;

        /// <summary>Creates the transaction.</summary>
        /// <param name="owner">The lock that handed it out.</param>
        public RecordingTransaction(RecordingApiKeyCreationLock owner)
        {
            _owner = owner;
        }

        /// <inheritdoc />
        public Guid TransactionId { get; } = Guid.NewGuid();

        /// <inheritdoc />
        public void Commit()
        {
            _owner.Commits++;
        }

        /// <inheritdoc />
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Commit();

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Rollback()
        {
        }

        /// <inheritdoc />
        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
