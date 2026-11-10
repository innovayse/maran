using Maran.Modules.ServerMigration.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="DiscoveryWarning"/> onto <c>servermigration.DiscoveryWarnings</c>.</summary>
public sealed class DiscoveryWarningConfiguration : IEntityTypeConfiguration<DiscoveryWarning>
{
    /// <summary>The widest an enum stored by name is.</summary>
    private const int EnumNameMaxLength = 32;

    /// <summary>Configures the table, columns, the cascade from the server and the index on it.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DiscoveryWarning> builder)
    {
        builder.ToTable("DiscoveryWarnings");

        builder.HasKey(warning => warning.Id);
        builder.Property(warning => warning.Id).ValueGeneratedNever();

        builder.Property(warning => warning.Kind).IsRequired().HasConversion<string>().HasMaxLength(EnumNameMaxLength);
        builder.Property(warning => warning.Subject).IsRequired().HasMaxLength(DiscoveryWarning.SubjectMaxLength);

        // Cascade: the warnings exist for exactly as long as the server does.
        builder.HasOne<RemoteServer>()
            .WithMany()
            .HasForeignKey(warning => warning.RemoteServerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(warning => warning.RemoteServerId).HasDatabaseName("IX_DiscoveryWarnings_RemoteServerId");
    }
}
