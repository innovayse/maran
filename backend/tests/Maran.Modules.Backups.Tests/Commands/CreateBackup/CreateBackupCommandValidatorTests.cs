using Maran.Modules.Backups.Commands.CreateBackup;

namespace Maran.Modules.Backups.Tests.Commands.CreateBackup;

/// <summary>What the create validator refuses, and what it must let through.</summary>
public sealed class CreateBackupCommandValidatorTests
{
    /// <summary>An empty account id is refused as a malformed request.</summary>
    [Fact]
    public void An_empty_account_id_is_refused()
    {
        var result = new CreateBackupCommandValidator()
            .Validate(new CreateBackupCommand(Guid.Empty, "203.0.113.7", "tests"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.PropertyName == nameof(CreateBackupCommand.AccountId);
        });
    }

    /// <summary>An ordinary account id is accepted.</summary>
    /// <remarks>
    /// The inverse control: a validator mutated to refuse everything satisfies the test above and
    /// only fails here.
    /// </remarks>
    [Fact]
    public void An_ordinary_account_id_is_accepted()
    {
        var result = new CreateBackupCommandValidator()
            .Validate(new CreateBackupCommand(Guid.NewGuid(), "203.0.113.7", "tests"));

        Assert.True(result.IsValid);
    }
}
