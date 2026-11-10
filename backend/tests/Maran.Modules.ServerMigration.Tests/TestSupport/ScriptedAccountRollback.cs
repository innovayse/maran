using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>The account rollback with its outcome set by the test; it records which accounts it was asked to delete.</summary>
public sealed class ScriptedAccountRollback : IAccountMigrationRollback
{
    /// <summary>The error the deletion answers, or <c>null</c> to succeed.</summary>
    public Error? FailWith { get; set; }

    /// <summary>The accounts it was asked to delete, in order.</summary>
    public List<Guid> Deleted { get; } = [];

    /// <inheritdoc />
    public Task<Result> DeleteImportedAccountAsync(
        Guid accountId,
        DateTimeOffset importStartedAt,
        CancellationToken cancellationToken)
    {
        Deleted.Add(accountId);

        return Task.FromResult(FailWith is null ? Result.Ok() : Result.Fail(FailWith));
    }
}
