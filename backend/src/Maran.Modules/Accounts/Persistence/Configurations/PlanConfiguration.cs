using System.Linq.Expressions;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Accounts.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Plan"/> onto the <c>accounts.Plans</c> table.</summary>
public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    /// <summary>Configures the table, keys, and column constraints for <see cref="Plan"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere") —
        // never the provider's lowercase default.
        builder.ToTable("Plans", table =>
        {
            // A plan an operator named carries its name in DisplayNameKey too, so the previous release,
            // which reads only that column, still names it (see Plan.DisplayNameKey). The entity keeps
            // the two equal; the constraint keeps them equal against a row written any other way.
            table.HasCheckConstraint("CK_Plans_NameMirroredInKey", "\"Name\" IS NULL OR \"DisplayNameKey\" = \"Name\"");
        });
        builder.HasKey(p => p.Id);

        builder.Property(p => p.DisplayNameKey)
            .IsRequired()
            .HasMaxLength(128);

        // Null for a plan the panel ships, whose name is a resx entry (see Plan.Name).
        builder.Property(p => p.Name)
            .HasMaxLength(Plan.NameMaxLength);

        builder.Property(p => p.DiskQuotaMb)
            .IsRequired();

        builder.Property(p => p.MaxSites)
            .IsRequired();

        builder.Property(p => p.MaxDatabases)
            .IsRequired();

        builder.Property(p => p.MaxSftpUsers)
            .IsRequired();

        builder.Property(p => p.MaxFtpUsers)
            .IsRequired();

        builder.Property(p => p.MaxCronEntries)
            .IsRequired();

        // The DNS zone allowance (issue #178). No HasDefaultValue on purpose: EF reads a default as "unset
        // means use the database's", which would turn a plan that deliberately sells zero zones into ten. The
        // migration gives every existing row the default, and the entity supplies it for a new one.
        builder.Property(p => p.MaxDnsZones)
            .IsRequired();

        builder.Property(p => p.MaxPhpWorkersPerPool)
            .IsRequired();

        // The feature set is one bigint of bits (see AccountFeature). No HasDefaultValue on purpose:
        // for a non-nullable property EF reads the CLR default (None, the empty set) as "not set" and
        // would write the database default instead, so a plan that sells nothing could not be saved.
        // Rows that existed before the column are given every feature by the migration's own default.
        builder.Property(p => p.Features)
            .IsRequired();

        // Owned rather than a complex type: the module's tests run on the in-memory provider, which
        // does not materialise complex types, and an owned value is split into this same table either
        // way. Every column is named here, because the provider's default (RuntimeLimits_Cpu..._Kind)
        // would put an implementation detail into a schema an operator reads with psql.
        //
        // Each ceiling is three columns while the change from "zero means unlimited" is in its expand
        // phase (rules/architecture.md): the nullable figure the code reads (null is no cap), and the
        // two columns of the previous release, which the value still writes and nothing reads, so a
        // panel rolled back to that release finds its ceilings where it looks. The old pair is dropped
        // in a later release.
        //
        // Backfill, run by the migration that adds the three nullable columns (quoted identifiers,
        // rules/csharp.md "Database naming"; raw SQL is approved with the `// raw-sql:` comment there):
        //   UPDATE accounts."Plans" SET
        //     "CpuQuotaPercentCeiling" = CASE WHEN "CpuLimitKind" = 'Limited' THEN "CpuQuotaPercent" END,
        //     "MemoryMaxMbCeiling"     = CASE WHEN "MemoryLimitKind" = 'Limited' THEN "MemoryMaxMb" END,
        //     "MaxProcessesCeiling"    = CASE WHEN "ProcessLimitKind" = 'Limited' THEN "MaxProcesses" END;
        // The CASE has no ELSE, so an unlimited ceiling (and its stored zero) becomes NULL. The Down
        // method drops the three columns only; the old pair was never touched.
        builder.OwnsOne(p => p.RuntimeLimits, limits =>
        {
            MapCeiling(limits, l => l.CpuQuotaPercent, "CpuQuotaPercentCeiling", "CpuLimitKind", "CpuQuotaPercent");
            MapCeiling(limits, l => l.MemoryMaxMb, "MemoryMaxMbCeiling", "MemoryLimitKind", "MemoryMaxMb");
            MapCeiling(limits, l => l.MaxProcesses, "MaxProcessesCeiling", "ProcessLimitKind", "MaxProcesses");
        });
        builder.Navigation(p => p.RuntimeLimits).IsRequired();

        // The PHP bounds are four plain columns, named here for the reason the runtime ceilings' are.
        // They are NOT NULL with database defaults equal to PhpLimits.Default (256, 64, 120, 3000) set
        // by the migration that adds them, so a plan that predates them reads as the conservative set
        // and the previous release, which ignores the columns, keeps working against the new schema.
        builder.OwnsOne(p => p.PhpLimits, limits =>
        {
            limits.Property(l => l.MemoryLimitMaxMb).HasColumnName("PhpMemoryLimitMaxMb").IsRequired();
            limits.Property(l => l.UploadMaxMb).HasColumnName("PhpUploadMaxMb").IsRequired();
            limits.Property(l => l.MaxExecutionSeconds).HasColumnName("PhpMaxExecutionSeconds").IsRequired();
            limits.Property(l => l.MaxInputVars).HasColumnName("PhpMaxInputVars").IsRequired();
        });
        builder.Navigation(p => p.PhpLimits).IsRequired();

        // The mail figures are four plain columns, named here for the reason the PHP bounds' are. They
        // are NOT NULL with database defaults equal to MailLimits.Default (10, 1024, 100, 10240) set by
        // the migration that adds them, so a plan that predates mail reads as the default set and the
        // previous release, which ignores the columns, keeps working against the new schema.
        builder.OwnsOne(p => p.MailLimits, limits =>
        {
            limits.Property(l => l.MaxMailboxes).HasColumnName("MaxMailboxes").IsRequired();
            limits.Property(l => l.DefaultMailboxQuotaMib).HasColumnName("DefaultMailboxQuotaMib").IsRequired();
            limits.Property(l => l.MailboxSendLimitPerHour).HasColumnName("MailboxSendLimitPerHour").IsRequired();
            limits.Property(l => l.MailboxQuotaPoolMib).HasColumnName("MailboxQuotaPoolMib").IsRequired();
        });
        builder.Navigation(p => p.MailLimits).IsRequired();

        // A plan's display-name key is its human-visible identity; two plans sharing one would be
        // indistinguishable to a customer choosing between them. Because an operator's plan mirrors
        // its name into this column, this one index also makes operators' names unique. It is
        // exact-match: the handlers refuse a name that differs only by case, or that reads the same
        // as a shipped plan's translated name, before the database is asked.
        builder.HasIndex(p => p.DisplayNameKey).IsUnique().HasDatabaseName("IX_Plans_DisplayNameKey");

        // A name is unique per owner. Two partial indexes and not one over (ResellerId, Name), because
        // PostgreSQL treats nulls as distinct in a unique index and two administrator plans with one
        // name would then both be accepted; NULLS NOT DISTINCT would close that in one index but is
        // PostgreSQL 15 and later, and the supported floor is 13 (rules/architecture.md). A shipped
        // plan has no Name (null), and nulls never collide.
        //
        // IX_Plans_DisplayNameKey above stays for one more release: the previous release's
        // CreatePlan relies on it to detect a duplicate name, and dropping it now would break the
        // promise that the previous release runs against the new schema. While it exists, a
        // reseller's plan cannot share a name with ANY other owner's plan; removing it is a
        // contract-phase migration of its own.
        builder.HasIndex(p => p.Name)
            .IsUnique()
            .HasFilter("\"ResellerId\" IS NULL")
            .HasDatabaseName("UX_Plans_Name_Unowned");
        builder.HasIndex(p => new { p.ResellerId, p.Name })
            .IsUnique()
            .HasFilter("\"ResellerId\" IS NOT NULL")
            .HasDatabaseName("UX_Plans_ResellerId_Name");

        // Optional (null is the administrator's plan, so the migration needs no backfill), and
        // RESTRICT, as the account's: a reseller that still owns a plan cannot be deleted from under
        // it. Declared without a navigation, as the account's plan is. No plain index on ResellerId:
        // the composite one above leads with it and already covers every owned row.
        builder.HasOne<Reseller>()
            .WithMany()
            .HasForeignKey(p => p.ResellerId)
            .HasConstraintName("FK_Plans_Resellers_ResellerId")
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>Maps one runtime ceiling onto its three columns of the plan's own table.</summary>
    /// <param name="limits">The owned runtime limits being configured.</param>
    /// <param name="ceiling">Which of the three ceilings.</param>
    /// <param name="ceilingColumn">The nullable column the code reads: the cap, or null when there is none.</param>
    /// <param name="legacyKindColumn">The previous release's column for whether the ceiling is set; written, never read.</param>
    /// <param name="legacyMaximumColumn">The previous release's column for the figure, zero when unlimited; written, never read.</param>
    private static void MapCeiling(
        OwnedNavigationBuilder<Plan, RuntimeLimits> limits,
        Expression<Func<RuntimeLimits, ResourceLimit?>> ceiling,
        string ceilingColumn,
        string legacyKindColumn,
        string legacyMaximumColumn)
    {
        limits.OwnsOne(ceiling, owned =>
        {
            owned.Property(l => l.Maximum).HasColumnName(ceilingColumn);

            // Field-only properties: the value derives both from its cap and exposes neither.
            owned.Property<ResourceLimitKind>("_legacyKind")
                .HasColumnName(legacyKindColumn)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();
            owned.Property<int>("_legacyMaximum").HasColumnName(legacyMaximumColumn).IsRequired();
        });
        limits.Navigation(ceiling).IsRequired();
    }
}
