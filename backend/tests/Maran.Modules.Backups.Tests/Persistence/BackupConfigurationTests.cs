using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Persistence;

/// <summary>How the backup row's verification columns are mapped, and that what is written is read back.</summary>
public sealed class BackupConfigurationTests
{
    /// <summary>The verification state is stored as the member's name and defaults to not verified.</summary>
    /// <remarks>
    /// Read from the model rather than from a database, because the default and the conversion are
    /// what the migration is scaffolded from: a row the previous release wrote, which never sets the
    /// column, must read as not verified, and a name in <c>psql</c> must say what it means.
    /// </remarks>
    [Fact]
    public void The_verification_state_is_stored_by_name_and_defaults_to_not_verified()
    {
        using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), Guid.NewGuid().ToString());
        var property = context.Model.FindEntityType(typeof(Backup))!.FindProperty(nameof(Backup.VerificationState))!;

        Assert.False(property.IsNullable);
        Assert.Equal(typeof(string), property.GetProviderClrType());
        Assert.Equal(BackupVerificationState.NotVerified, property.GetDefaultValue());
    }

    /// <summary>The verified instant and the code are nullable and the code is bounded, so the migration expands.</summary>
    [Fact]
    public void The_verification_instant_and_code_are_nullable_and_the_code_is_bounded()
    {
        using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), Guid.NewGuid().ToString());
        var entity = context.Model.FindEntityType(typeof(Backup))!;

        Assert.True(entity.FindProperty(nameof(Backup.VerifiedAt))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(Backup.VerificationFailureCode))!.IsNullable);
        Assert.Equal(64, entity.FindProperty(nameof(Backup.VerificationFailureCode))!.GetMaxLength());
    }

    /// <summary>A failed verification survives a save and a fresh read.</summary>
    [Fact]
    public async Task A_failed_verification_survives_a_save_and_a_fresh_read()
    {
        var database = Guid.NewGuid().ToString();
        var account = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(account);
        var at = new DateTimeOffset(2026, 11, 4, 3, 0, 0, TimeSpan.Zero);
        row.MarkVerificationFailed(at, "AgentValidationFailed");

        await using (var writing = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            writing.Backups.Add(row);
            await writing.SaveChangesAsync();
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var read = await reading.Backups.SingleAsync();

        Assert.Equal(BackupVerificationState.Failed, read.VerificationState);
        Assert.Equal("AgentValidationFailed", read.VerificationFailureCode);
        Assert.Equal(at, read.VerifiedAt);
    }
}
