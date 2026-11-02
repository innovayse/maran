using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
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
        builder.ToTable("BackupSchedules", table =>
        {
            // A weekly cadence names its weekday and a daily one has none: the database refuses the
            // pair the entity could never have written (see BackupSchedule.Cadence).
            table.HasCheckConstraint(
                "CK_BackupSchedules_WeekdayOnlyWhenWeekly",
                "(\"Frequency\" = 'Weekly') = (\"DayOfWeekUtc\" IS NOT NULL)");

            // The schedule is enabled exactly when it remembers when it was switched on. The legacy
            // Enabled column (below) is kept equal to that fact by the entity; the constraint keeps
            // them equal against a row written any other way.
            table.HasCheckConstraint(
                "CK_BackupSchedules_EnabledMatchesEnabledAt",
                "\"Enabled\" = (\"EnabledAt\" IS NOT NULL)");
        });
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

        // The cadence is stored in these three existing columns and is reachable only through
        // BackupSchedule.Cadence, a union (daily at an hour, or weekly on a weekday at an hour), so
        // the columns are private properties mapped by name and Cadence itself is not mapped.
        //
        // Expand phase of replacing the (Frequency, DayOfWeekUtc) pair with that union: no column is
        // added, because the three columns already hold exactly a union's data; what was missing was
        // the rule that a weekly row names its weekday and a daily one does not (the check constraint
        // above). Rows that broke the rule were read as daily, so the backfill states that reading.
        //
        // raw-sql backfill, written into the migration's Up BEFORE the check constraints are created
        // (identifiers quoted: PascalCase, rules/csharp.md):
        //   UPDATE backups."BackupSchedules" SET "Frequency" = 'Daily'
        //     WHERE "Frequency" = 'Weekly' AND "DayOfWeekUtc" IS NULL;
        //   UPDATE backups."BackupSchedules" SET "DayOfWeekUtc" = NULL
        //     WHERE "Frequency" = 'Daily' AND "DayOfWeekUtc" IS NOT NULL;
        //
        // Expand phase of replacing the Enabled flag with EnabledAt alone. The flag's column is kept,
        // still written by the entity in step with EnabledAt and read by nothing; the next run of a
        // row holding the flag without the instant was never computed (the schedule read as off), so
        // the backfill makes the flag say so, and clears an instant that a switched-off row kept:
        //   UPDATE backups."BackupSchedules" SET "Enabled" = FALSE WHERE "Enabled" AND "EnabledAt" IS NULL;
        //   UPDATE backups."BackupSchedules" SET "EnabledAt" = NULL WHERE NOT "Enabled" AND "EnabledAt" IS NOT NULL;
        // Down needs no data statement: the previous release reads Enabled, which stays written.
        builder.Ignore(schedule => schedule.Cadence);

        builder.Property<BackupFrequency>("Frequency")
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        builder.Property<int>("HourUtc")
            .IsRequired();

        // The BCL's DayOfWeek, stored as its name for the same reason and nullable for a daily
        // schedule, which has no weekday to name.
        builder.Property<DayOfWeek?>("DayOfWeekUtc")
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        builder.Property(schedule => schedule.RetainCount)
            .IsRequired();

        // The previous release's flag: still mapped, still written by the entity in step with
        // EnabledAt, never read. Dropped in a later release (contract phase).
        builder.Property<bool>("LegacyEnabled")
            .HasColumnName("Enabled")
            .IsRequired();

        // Two columns, two facts: when the schedule was switched on (null while it is off, which is
        // the whole of "enabled"), and when a run last started.
        builder.Property(schedule => schedule.EnabledAt);

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
