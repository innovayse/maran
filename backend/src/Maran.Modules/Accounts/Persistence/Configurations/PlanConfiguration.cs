using Maran.Modules.Accounts.Domain.Entities;
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
        builder.ToTable("Plans");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.DisplayNameKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.DiskQuotaMb)
            .IsRequired();

        builder.Property(p => p.MaxSites)
            .IsRequired();

        builder.Property(p => p.MaxDatabases)
            .IsRequired();

        builder.Property(p => p.MaxSftpUsers)
            .IsRequired();

        builder.Property(p => p.MaxCronEntries)
            .IsRequired();

        builder.Property(p => p.MaxPhpWorkersPerPool)
            .IsRequired();

        // A plan's display-name key is its human-visible identity; two plans sharing one would be
        // indistinguishable to a customer choosing between them.
        builder.HasIndex(p => p.DisplayNameKey).IsUnique().HasDatabaseName("IX_Plans_DisplayNameKey");
    }
}
