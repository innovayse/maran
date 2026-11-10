using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Tests.Domain.Entities;

/// <summary>How an import moves between states, and that its generated credentials are claimed once (issue #127).</summary>
public sealed class AccountImportTests
{
    /// <summary>The moment the imports below are made.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A staged import for the tests to act on.</summary>
    /// <returns>A new import.</returns>
    private static AccountImport NewImport()
    {
        return new AccountImport(
            Guid.NewGuid(),
            "alice",
            "alice.example",
            "source.example",
            new string('b', 64),
            "[]",
            1024,
            new MigrationImportedArtifact(Guid.NewGuid(), 2048, new string('c', 64), 1),
            Guid.NewGuid(),
            Now);
    }

    /// <summary>A staged import starts once.</summary>
    [Fact]
    public void A_staged_import_starts_and_cannot_start_twice()
    {
        var import = NewImport();

        Assert.True(import.Start(Guid.NewGuid(), null, Now));
        Assert.Equal(AccountImportState.Running, import.State);
        Assert.False(import.Start(Guid.NewGuid(), null, Now));
    }

    /// <summary>Only a staged import can be discarded.</summary>
    [Fact]
    public void Only_a_staged_import_can_be_discarded()
    {
        var staged = NewImport();
        var running = NewImport();
        running.Start(Guid.NewGuid(), null, Now);

        Assert.True(staged.Discard(Now));
        Assert.False(running.Discard(Now));
        Assert.Equal(AccountImportState.Running, running.State);
    }

    /// <summary>The credentials are returned a single time.</summary>
    [Fact]
    public void Issued_secrets_can_be_claimed_only_once()
    {
        var import = NewImport();
        import.HoldIssuedSecrets("[{\"subject\":\"db\",\"secret\":\"x\"}]");

        Assert.NotNull(import.ClaimIssuedSecrets());
        Assert.Null(import.ClaimIssuedSecrets());
    }

    /// <summary>A rollback, whether complete or not, discards the credentials the job had generated.</summary>
    [Fact]
    public void A_rollback_discards_the_issued_secrets()
    {
        var complete = NewImport();
        var incomplete = NewImport();
        complete.HoldIssuedSecrets("[]");
        incomplete.HoldIssuedSecrets("[]");

        complete.RollBack("Failed", Now);
        incomplete.RollbackFailed("Failed", Now);

        Assert.Null(complete.IssuedSecretsJson);
        Assert.Null(incomplete.IssuedSecretsJson);
        Assert.Equal(AccountImportState.RolledBack, complete.State);
        Assert.Equal(AccountImportState.RolledBackIncomplete, incomplete.State);
    }
}
