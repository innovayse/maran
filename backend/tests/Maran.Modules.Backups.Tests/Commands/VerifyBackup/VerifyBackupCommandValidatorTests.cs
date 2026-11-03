using Maran.Modules.Backups.Commands.VerifyBackup;

namespace Maran.Modules.Backups.Tests.Commands.VerifyBackup;

/// <summary>What the verify validator refuses, and what it must let through.</summary>
public sealed class VerifyBackupCommandValidatorTests
{
    /// <summary>An empty backup id is refused as a malformed request.</summary>
    [Fact]
    public void An_empty_backup_id_is_refused()
    {
        var result = new VerifyBackupCommandValidator()
            .Validate(new VerifyBackupCommand(Guid.Empty, "203.0.113.7", "tests"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.PropertyName == nameof(VerifyBackupCommand.BackupId);
        });
    }

    /// <summary>An ordinary backup id is accepted.</summary>
    /// <remarks>
    /// The inverse control: a validator mutated to refuse everything satisfies the test above and
    /// only fails here.
    /// </remarks>
    [Fact]
    public void An_ordinary_backup_id_is_accepted()
    {
        var result = new VerifyBackupCommandValidator()
            .Validate(new VerifyBackupCommand(Guid.NewGuid(), "203.0.113.7", "tests"));

        Assert.True(result.IsValid);
    }
}
