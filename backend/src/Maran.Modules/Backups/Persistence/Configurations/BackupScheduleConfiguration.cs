using Maran.Modules.Backups.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Backups.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="BackupSchedule"/> onto the <c>backups.BackupSchedules</c> table.
/// </summary>
public sealed class BackupScheduleConfiguration : IEntityTypeConfiguration<BackupSchedule>
{
    /// <summary>The longest an enum member's persisted name may be.</summary>
    private const int EnumNameMaxLength = 16;

    /// <summary>Configures the table, keys, and column constraints for <see cref="BackupSchedule"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<BackupSchedule> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming"), never the provider's default.
        builder.ToTable("BackupSchedules");
        builder.HasKey(schedule => schedule.Id);

        // Nullable: a schedule naming no account is the host-wide policy.
        builder.Property(schedule => schedule.AccountId);

        // Stored as the member's NAME rather than its number, so a row read in psql says what it is
        // and adding a member later cannot renumber what is already written.
        // Nullable, with null meaning the default destination — the same convention Backup uses, so
        // "no destination named" reads one way across the module. Restrict rather than a cascade:
        // removing a destination out from under a schedule would silently repoint the schedule's
        // nightly run rather than making the operator decide.
        builder.Property(schedule => schedule.DestinationId);

        builder.HasOne<BackupDestination>()
            .WithMany()
            .HasForeignKey(schedule => schedule.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(schedule => schedule.Frequency)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        builder.Property(schedule => schedule.HourUtc)
            .IsRequired();

        // The BCL's DayOfWeek, stored as its name for the same reason and nullable for a daily
        // schedule, which has no weekday to name.
        builder.Property(schedule => schedule.DayOfWeekUtc)
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        builder.Property(schedule => schedule.RetainCount)
            .IsRequired();

        builder.Property(schedule => schedule.Enabled)
            .IsRequired();

        builder.Property(schedule => schedule.LastRunAt);

        // At most one schedule per account: two would take two backups a night and neither of them
        // would be wrong, so the second is refused by the database rather than by whichever handler
        // remembers to look. Partial, because PostgreSQL counts NULLs as distinct in a unique index
        // and the host-wide row's AccountId is NULL — without the filter the index would be a rule
        // about the per-account rows wearing a name that claims to cover all of them.
        //
        // The HOST-WIDE singleton is therefore NOT enforced here, and that is stated rather than
        // implied: PostgreSQL can express it (NULLS NOT DISTINCT), but only from version 15, and
        // Ubuntu 22.04 — a system rules/architecture.md obliges this product to work on — ships 14.
        // An index that fails to create on a supported platform is worse than a rule the upsert
        // holds up, so SaveBackupScheduleCommandHandler reads the existing row before inserting and
        // BackupScheduleSingletonTests is what pins that it does.
        builder.HasIndex(schedule => schedule.AccountId)
            .IsUnique()
            .HasFilter("\"AccountId\" IS NOT NULL")
            .HasDatabaseName("UX_BackupSchedules_AccountId");
    }
}
