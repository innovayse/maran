using System.Linq.Expressions;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.SharedKernel.Constants;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Accounts.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Reseller"/> onto the <c>accounts.Resellers</c> table.</summary>
public sealed class ResellerConfiguration : IEntityTypeConfiguration<Reseller>
{
    /// <summary>Configures the table, keys, envelope columns and concurrency token for <see cref="Reseller"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Reseller> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("Resellers");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(Reseller.NameMaxLength);

        builder.Property(r => r.ContactEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.Language)
            .IsRequired()
            .HasMaxLength(8)
            .HasDefaultValue(PanelLanguages.English);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        // The row's concurrency token (Reseller.AllocationRevision): two creates racing for the last
        // free slot both update this row, and the second one's WHERE no longer matches. No database
        // default on purpose, for the reason Plan.Features gives: the CLR default of zero is a real
        // value here and EF would otherwise read it as "not set".
        builder.Property(r => r.AllocationRevision)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        // The name is the reseller's login name, so it is unique here as a defence against a race;
        // the one namespace it shares with account names is checked by the command handlers.
        builder.HasIndex(r => r.Name).IsUnique().HasDatabaseName("IX_Resellers_Name");

        // Owned, as the plan's runtime limits are: the module's tests run on the in-memory provider,
        // which does not materialise complex types, and the value is split into this same table. Every
        // column is named here so the schema an operator reads with psql carries no provider default.
        builder.OwnsOne(r => r.Envelope, envelope =>
        {
            envelope.Property(e => e.MaxAccounts).HasColumnName("MaxAccounts").IsRequired();
            envelope.Property(e => e.DiskPoolMb).HasColumnName("DiskPoolMb").IsRequired();
            envelope.Property(e => e.MaxPlans).HasColumnName("MaxPlans").IsRequired();
            envelope.Property(e => e.MaxDiskQuotaMb).HasColumnName("MaxDiskQuotaMb").IsRequired();
            envelope.Property(e => e.MaxSites).HasColumnName("MaxSites").IsRequired();
            envelope.Property(e => e.MaxDatabases).HasColumnName("MaxDatabases").IsRequired();
            envelope.Property(e => e.MaxSftpUsers).HasColumnName("MaxSftpUsers").IsRequired();
            envelope.Property(e => e.MaxFtpUsers).HasColumnName("MaxFtpUsers").IsRequired();
            envelope.Property(e => e.MaxCronEntries).HasColumnName("MaxCronEntries").IsRequired();
            envelope.Property(e => e.MaxPhpWorkersPerPool).HasColumnName("MaxPhpWorkersPerPool").IsRequired();
            envelope.Property(e => e.Features).HasColumnName("Features").IsRequired();

            // Each runtime ceiling is ONE nullable column: the cap, or null when the envelope states
            // none. The plan's two legacy columns per ceiling are not repeated: this table is new, so
            // no previous release reads it.
            envelope.OwnsOne(e => e.RuntimeLimits, limits =>
            {
                MapCeiling(limits, l => l.CpuQuotaPercent, "MaxCpuQuotaPercent");
                MapCeiling(limits, l => l.MemoryMaxMb, "MaxMemoryMb");
                MapCeiling(limits, l => l.MaxProcesses, "MaxProcesses");
            });
            envelope.Navigation(e => e.RuntimeLimits).IsRequired();
        });
        builder.Navigation(r => r.Envelope).IsRequired();
    }

    /// <summary>Maps one envelope runtime ceiling onto its single nullable column of the reseller's own table.</summary>
    /// <param name="limits">The owned runtime limits being configured.</param>
    /// <param name="ceiling">Which of the three ceilings.</param>
    /// <param name="column">The nullable column holding the cap, or null when there is none.</param>
    private static void MapCeiling(
        OwnedNavigationBuilder<ResellerEnvelope, RuntimeLimits> limits,
        Expression<Func<RuntimeLimits, ResourceLimit?>> ceiling,
        string column)
    {
        limits.OwnsOne(ceiling, owned =>
        {
            owned.Property(l => l.Maximum).HasColumnName(column);
        });
        limits.Navigation(ceiling).IsRequired();
    }
}
