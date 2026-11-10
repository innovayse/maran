using Maran.Modules.Marketplace.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Marketplace.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ModuleVersionFloor"/> onto the <c>marketplace.ModuleVersionFloors</c>
/// table: one row per module, keyed by the module's id.
/// </summary>
public sealed class ModuleVersionFloorConfiguration : IEntityTypeConfiguration<ModuleVersionFloor>
{
    /// <summary>The longest module id a manifest may carry.</summary>
    private const int ModuleIdMaxLength = 64;

    /// <summary>The longest semantic version text the panel parses.</summary>
    private const int VersionMaxLength = 128;

    /// <summary>Configures the table, key and columns for <see cref="ModuleVersionFloor"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<ModuleVersionFloor> builder)
    {
        builder.ToTable("ModuleVersionFloors");

        builder.HasKey(floor => floor.ModuleId);
        builder.Property(floor => floor.ModuleId)
            .HasMaxLength(ModuleIdMaxLength)
            .ValueGeneratedNever();

        builder.Property(floor => floor.HighestVersionSeen)
            .IsRequired()
            .HasMaxLength(VersionMaxLength);

        builder.Property(floor => floor.UpdatedAt)
            .IsRequired();
    }
}
