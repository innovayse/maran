using Maran.Modules.Apps.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Apps.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="InstalledApp"/> onto the <c>apps.InstalledApps</c> table.</summary>
public sealed class InstalledAppConfiguration : IEntityTypeConfiguration<InstalledApp>
{
    /// <summary>The longest catalogue id: <c>nodejs-</c> and a two-digit major, with room to spare.</summary>
    private const int AppIdMaxLength = 32;

    /// <summary>The longest version text a host reports for a package.</summary>
    private const int VersionMaxLength = 64;

    /// <summary>Configures the table, keys, and column constraints for <see cref="InstalledApp"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<InstalledApp> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("InstalledApps");
        builder.HasKey(app => app.Id);

        builder.Property(app => app.AppId)
            .IsRequired()
            .HasMaxLength(AppIdMaxLength);

        builder.Property(app => app.Kind)
            .IsRequired();

        builder.Property(app => app.Version)
            .IsRequired()
            .HasMaxLength(VersionMaxLength);

        builder.Property(app => app.State)
            .IsRequired();

        builder.Property(app => app.UpdatedAt)
            .IsRequired();

        // One row per app: the loser of two concurrent installs of the same app is refused here.
        builder.HasIndex(app => app.AppId)
            .IsUnique()
            .HasDatabaseName("IX_InstalledApps_AppId");
    }
}
