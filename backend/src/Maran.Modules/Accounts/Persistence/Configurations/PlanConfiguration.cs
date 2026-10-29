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

        // A plan's display-name key is its human-visible identity; two plans sharing one would be
        // indistinguishable to a customer choosing between them. Because an operator's plan mirrors
        // its name into this column, this one index also makes operators' names unique. It is
        // exact-match: the handlers refuse a name that differs only by case, or that reads the same
        // as a shipped plan's translated name, before the database is asked.
        builder.HasIndex(p => p.DisplayNameKey).IsUnique().HasDatabaseName("IX_Plans_DisplayNameKey");
    }
}
