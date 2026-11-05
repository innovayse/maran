using Maran.Modules.Sftp.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>
/// The per-login key lock as this project can run it: it records which login was locked and hands
/// back a <see cref="RecordingDbContextTransaction"/>, without the PostgreSQL <c>FOR UPDATE</c> the
/// real one takes.
/// </summary>
/// <remarks>
/// <b>UNOBSERVED HERE: that two simultaneous changes on one login are serialised.</b> That is a
/// property of the row lock, invisible to the in-memory provider; what this double lets a test
/// assert is that the handler asked for the lock on the right login before it read the key set.
/// </remarks>
public sealed class StubSftpUserKeyLock : ISftpUserKeyLock
{
    /// <summary>The logins locked, in order.</summary>
    public List<Guid> Locked { get; } = [];

    /// <summary>Every transaction handed out, in order.</summary>
    public List<RecordingDbContextTransaction> Transactions { get; } = [];

    /// <summary>Runs when a lock is taken, so a test can observe the order of calls.</summary>
    public Action? OnBegin { get; set; }

    /// <summary>When set, the exception every commit on a transaction handed out afterwards throws.</summary>
    public Exception? CommitFailure { get; set; }

    /// <inheritdoc/>
    public Task<IDbContextTransaction> BeginAsync(Guid sftpUserId, CancellationToken cancellationToken)
    {
        Locked.Add(sftpUserId);

        var transaction = new RecordingDbContextTransaction { CommitFailure = CommitFailure };
        Transactions.Add(transaction);
        OnBegin?.Invoke();

        return Task.FromResult<IDbContextTransaction>(transaction);
    }
}
