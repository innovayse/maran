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

    /// <summary>The name of the unique index that allows one active site web server (issue #526).</summary>
    public const string ActiveWebServerIndexName = "IX_InstalledApps_ActiveWebServer";

    /// <summary>The name of the unique index that allows one row per app.</summary>
    public const string AppIdIndexName = "IX_InstalledApps_AppId";

    /// <summary>
    /// The rows that hold the web server ports: a web server that is installing, installed or being removed.
    /// Kept beside the index it feeds, with the numbers of <c>AppKind.WebServer</c> (1) and of
    /// <c>AppState.Installing</c> (1), <c>Installed</c> (2) and <c>Removing</c> (4).
    /// </summary>
    private const string ActiveWebServerFilter = "\"Kind\" = 1 AND \"State\" IN (1, 2, 4)";

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

        // The optimistic-concurrency token: every transition moves it, so two requests that read the same
        // state and each begin an operation cannot both be saved.
        builder.Property(app => app.Revision)
            .IsRequired()
            .IsConcurrencyToken();

        // One row per app: the loser of two concurrent installs of the same app is refused here.
        builder.HasIndex(app => app.AppId)
            .IsUnique()
            .HasDatabaseName(AppIdIndexName);

        // One site web server at a time, enforced where it cannot race: nginx and apache are different
        // app ids, so the index above never sees them together. Only the rows that hold the ports count;
        // a failed web server holds nothing and may sit beside a new one.
        builder.HasIndex(app => app.Kind)
            .IsUnique()
            .HasFilter(ActiveWebServerFilter)
            .HasDatabaseName(ActiveWebServerIndexName);
    }
}
