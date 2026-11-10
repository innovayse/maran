using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Domain.Policies;

namespace Maran.Modules.Backups.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="AccountBackupAllowance"/> (issues #478 and #479).</summary>
public sealed class AccountBackupAllowanceTests
{
    /// <summary>The instant every row is stamped with.</summary>
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a completed backup of a kind.</summary>
    /// <param name="kind">Why the backup was taken.</param>
    /// <returns>The row.</returns>
    private static Backup Completed(BackupKind kind)
    {
        var backup = new Backup(Guid.NewGuid(), Guid.NewGuid(), destinationId: null, kind, At);
        backup.Completed(1, new string('a', 64), 0, At.AddMinutes(1));

        return backup;
    }

    /// <summary>At the maximum there is no room; one below it there is.</summary>
    [Fact]
    public void Room_ends_exactly_at_the_maximum()
    {
        var nine = Enumerable.Range(0, AccountBackupAllowance.MaximumCustomerBackups - 1)
            .Select(_ => { return Completed(BackupKind.Manual); })
            .ToList();

        Assert.True(AccountBackupAllowance.HasRoomForManualBackup(nine));
        Assert.False(AccountBackupAllowance.HasRoomForManualBackup(nine.Append(Completed(BackupKind.PreRestore))));
    }

    /// <summary>The pre-restore bound is smaller than the allowance, so safety copies cannot fill it.</summary>
    [Fact]
    public void The_pre_restore_bound_leaves_room_for_manual_backups()
    {
        Assert.True(AccountBackupAllowance.MaximumPreRestoreCopies < AccountBackupAllowance.MaximumCustomerBackups);
    }
}
