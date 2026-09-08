using Maran.Modules.Backups.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Backups.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="BackupDestination"/> onto the <c>backups.BackupDestinations</c>
/// table.
/// </summary>
public sealed class BackupDestinationConfiguration : IEntityTypeConfiguration<BackupDestination>
{
    /// <summary>The longest an enum member's persisted name may be.</summary>
    private const int EnumNameMaxLength = 16;

    /// <summary>Configures the table, keys, and column constraints for <see cref="BackupDestination"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<BackupDestination> builder)
    {
        builder.ToTable("BackupDestinations");
        builder.HasKey(destination => destination.Id);

        builder.Property(destination => destination.Name)
            .IsRequired()
            .HasMaxLength(BackupDestination.NameMaxLength);

        // Stored as the member's NAME rather than its number, so a row read in psql says what it is
        // and adding a member later cannot renumber what is already written.
        builder.Property(destination => destination.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        // Required-with-an-empty-default rather than nullable, so "this kind records no path" has one
        // representation and a reader never has to decide whether NULL and '' mean the same thing.
        builder.Property(destination => destination.Path)
            .IsRequired()
            .HasMaxLength(BackupDestination.PathMaxLength)
            .HasDefaultValue(string.Empty);

        builder.Property(destination => destination.IsDefault)
            .IsRequired();

        builder.Property(destination => destination.CreatedAt)
            .IsRequired();

        builder.HasIndex(destination => destination.Name)
            .IsUnique()
            .HasDatabaseName("IX_BackupDestinations_Name");

        // Filtered, so the uniqueness is "at most one DEFAULT" rather than "at most two rows". A
        // plain unique index on a boolean would cap the whole table at two, which is a constraint
        // about the wrong thing that would read as this one.
        builder.HasIndex(destination => destination.IsDefault)
            .IsUnique()
            .HasFilter("\"IsDefault\"")
            .HasDatabaseName("IX_BackupDestinations_IsDefault");
    }
}
