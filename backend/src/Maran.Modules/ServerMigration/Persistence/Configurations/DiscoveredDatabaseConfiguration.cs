using Maran.Modules.ServerMigration.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="DiscoveredDatabase"/> onto <c>servermigration.DiscoveredDatabases</c>.</summary>
public sealed class DiscoveredDatabaseConfiguration : IEntityTypeConfiguration<DiscoveredDatabase>
{
    /// <summary>Configures the table, columns and the index on the owning account.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DiscoveredDatabase> builder)
    {
        builder.ToTable("DiscoveredDatabases");

        builder.HasKey(database => database.Id);
        builder.Property(database => database.Id).ValueGeneratedNever();

        builder.Property(database => database.Name).IsRequired().HasMaxLength(DiscoveredDatabase.NameMaxLength);
        builder.Property(database => database.SizeBytes).IsRequired();
        builder.Property(database => database.SizeKnown).IsRequired();

        builder.HasIndex(database => database.DiscoveredAccountId).HasDatabaseName("IX_DiscoveredDatabases_DiscoveredAccountId");
    }
}
