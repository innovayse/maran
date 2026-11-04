using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Tests.TestSupport;

namespace Maran.Modules.Backups.Tests.Mappers;

/// <summary>What the backup mapper carries onto the wire about a backup's verification.</summary>
public sealed class BackupMapperVerificationTests
{
    /// <summary>The instant the verification is stamped with.</summary>
    private static readonly DateTimeOffset VerifiedInstant = new(2026, 11, 4, 9, 30, 0, TimeSpan.Zero);

    /// <summary>A backup nobody has verified reads as not verified, with no date and no finding.</summary>
    [Fact]
    public void A_backup_nobody_has_verified_reads_as_not_verified()
    {
        var dto = Map(BackupsTestContext.CompletedRow(Guid.NewGuid()));

        Assert.Equal(BackupVerificationState.NotVerified, dto.VerificationState);
        Assert.Equal("Not verified", dto.VerificationStateName);
        Assert.Null(dto.VerifiedAt);
        Assert.Null(dto.VerificationFailureCode);
        Assert.Null(dto.VerificationFailureDisplayName);
    }

    /// <summary>A verified backup carries its state, its name and the date of the read.</summary>
    [Fact]
    public void A_verified_backup_carries_its_state_and_its_date()
    {
        var row = BackupsTestContext.CompletedRow(Guid.NewGuid());
        row.MarkVerified(VerifiedInstant);

        var dto = Map(row);

        Assert.Equal(BackupVerificationState.Verified, dto.VerificationState);
        Assert.Equal("Verified", dto.VerificationStateName);
        Assert.Equal(VerifiedInstant, dto.VerifiedAt);
        Assert.Null(dto.VerificationFailureCode);
    }

    /// <summary>A failed verification carries its code and its words beside the run's own, unchanged, status.</summary>
    [Fact]
    public void A_failed_verification_carries_its_code_and_its_words_and_leaves_the_run_alone()
    {
        var row = BackupsTestContext.CompletedRow(Guid.NewGuid());
        row.MarkVerificationFailed(VerifiedInstant, "BackupVerificationFailed");

        var dto = Map(row);

        Assert.Equal(BackupVerificationState.Failed, dto.VerificationState);
        Assert.Equal("BackupVerificationFailed", dto.VerificationFailureCode);
        Assert.Equal("The archive does not match its record", dto.VerificationFailureDisplayName);
        Assert.Equal(VerifiedInstant, dto.VerifiedAt);
        Assert.Equal(BackupStatus.Completed, dto.Status);
        Assert.Null(dto.FailureCode);
    }

    /// <summary>Maps one row the way the handlers do, with the real resource files behind the names.</summary>
    /// <param name="row">The row to map.</param>
    /// <returns>The wire shape.</returns>
    private static BackupDto Map(Backup row)
    {
        var failures = BackupsTestContext.FailureNames();
        var names = BackupsTestContext.BackupNames();

        return BackupMapper.From(
            row,
            new Dictionary<Guid, string>(),
            failures.Of(row.RunFailureCode),
            names.Of(row.Status),
            names.Of(row.Kind),
            names.Of(row.VerificationState),
            failures.Of(row.VerificationFailureCode));
    }
}
