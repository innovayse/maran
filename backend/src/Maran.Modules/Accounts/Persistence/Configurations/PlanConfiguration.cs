using System.Linq.Expressions;
using Maran.Modules.Accounts.Domain.Entities;
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

        builder.Property(p => p.MaxPhpWorkersPerPool)
            .IsRequired();

        // Owned rather than a complex type: the module's tests run on the in-memory provider, which
        // does not materialise complex types, and an owned value is split into this same table either
        // way. Every column is named here, because the provider's default (RuntimeLimits_Cpu..._Kind)
        // would put an implementation detail into a schema an operator reads with psql.
        builder.OwnsOne(p => p.RuntimeLimits, limits =>
        {
            MapCeiling(limits, l => l.CpuQuotaPercent, "CpuLimitKind", "CpuQuotaPercent");
            MapCeiling(limits, l => l.MemoryMaxMb, "MemoryLimitKind", "MemoryMaxMb");
            MapCeiling(limits, l => l.MaxProcesses, "ProcessLimitKind", "MaxProcesses");
        });
        builder.Navigation(p => p.RuntimeLimits).IsRequired();

        // A plan's display-name key is its human-visible identity; two plans sharing one would be
        // indistinguishable to a customer choosing between them. Because an operator's plan mirrors
        // its name into this column, this one index also makes operators' names unique. It is
        // exact-match: the handlers refuse a name that differs only by case, or that reads the same
        // as a shipped plan's translated name, before the database is asked.
        builder.HasIndex(p => p.DisplayNameKey).IsUnique().HasDatabaseName("IX_Plans_DisplayNameKey");
    }

    /// <summary>Maps one runtime ceiling onto its two columns of the plan's own table.</summary>
    /// <param name="limits">The owned runtime limits being configured.</param>
    /// <param name="ceiling">Which of the three ceilings.</param>
    /// <param name="kindColumn">The column holding whether the ceiling is set, as its enum name.</param>
    /// <param name="maximumColumn">The column holding the figure; zero when unlimited.</param>
    private static void MapCeiling(
        OwnedNavigationBuilder<Plan, RuntimeLimits> limits,
        Expression<Func<RuntimeLimits, ResourceLimit?>> ceiling,
        string kindColumn,
        string maximumColumn)
    {
        limits.OwnsOne(ceiling, owned =>
        {
            owned.Property(l => l.Kind).HasColumnName(kindColumn).HasConversion<string>().HasMaxLength(16).IsRequired();
            owned.Property(l => l.Maximum).HasColumnName(maximumColumn).IsRequired();
        });
        limits.Navigation(ceiling).IsRequired();
    }
}
