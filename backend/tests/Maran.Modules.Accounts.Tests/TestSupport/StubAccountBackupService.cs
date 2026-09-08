using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An <see cref="IAccountBackupService"/> double: it answers a scripted result and records what it
/// was asked to back up.
/// </summary>
/// <remarks>
/// It records the ACCOUNT NAME as well as the id, because the name is what the agent addresses the
/// backup by and it is read from the row the deletion is about to destroy — a deletion that passed
/// the wrong one would archive somebody else's home and no assertion on the id would notice.
/// </remarks>
public sealed class StubAccountBackupService : IAccountBackupService
{
    /// <summary>The answer every call gives.</summary>
    private readonly Result<Guid> _result;

    /// <summary>Creates a double that succeeds, reporting a fresh backup id.</summary>
    public StubAccountBackupService()
    {
        _result = Result<Guid>.Ok(Guid.NewGuid());
    }

    /// <summary>Creates a double that answers <paramref name="result"/>.</summary>
    /// <param name="result">The answer every call gives.</param>
    public StubAccountBackupService(Result<Guid> result)
    {
        _result = result;
    }

    /// <summary>Every account this service was asked to back up, in order.</summary>
    public List<(Guid AccountId, string AccountName)> Taken { get; } = [];

    /// <summary>A double that refuses, as a run that produced no usable archive does.</summary>
    /// <returns>The refusing double.</returns>
    public static StubAccountBackupService Failing()
    {
        return new StubAccountBackupService(
            Result<Guid>.Fail(Error.Of("FinalBackupFailed", ErrorType.Failure)));
    }

    /// <inheritdoc />
    public Task<Result<Guid>> TakeFinalBackupAsync(
        Guid accountId,
        string accountName,
        CancellationToken cancellationToken)
    {
        Taken.Add((accountId, accountName));
        return Task.FromResult(_result);
    }
}
